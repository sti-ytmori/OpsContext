using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Models;
using OpsContext.Agents.Tools;
using System.Threading.Channels;

namespace OpsContext.Agents.Services;

// design 06「CuratorHostedService」。
// BackgroundService (Singleton) として動作し、Channel から ConversationEvent を受け取って
// Detect → Draft → Refine の 3 段プロンプトを実行し判断を ContextEntries に永続化する。
// IContextStoreTool が Scoped の場合は IServiceScopeFactory 経由でスコープを作成する。
// 3 イベント処理ごとに FocusSnapshot を生成して FocusSnapshots テーブルに INSERT する。
public sealed class CuratorHostedService(
    ChannelReader<ConversationEvent> reader,
    IChatClient chatClient,
    IServiceScopeFactory scopeFactory,
    ILogger<CuratorHostedService> logger
) : BackgroundService
{
    // 3 イベントごとに FocusSnapshot を生成する（BackgroundService は逐次処理のためスレッドセーフ）
    private int _turnCounter;
    private const int FocusSnapshotInterval = 3;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        logger.LogInformation("CuratorHostedService started.");
        await foreach (var ev in reader.ReadAllAsync(ct))
        {
            try
            {
                // Scoped サービスを使うためスコープを生成する
                await using var scope = scopeFactory.CreateAsyncScope();
                var contextStore = scope.ServiceProvider.GetRequiredService<IContextStoreTool>();
                await ProcessAsync(ev, contextStore, ct);

                _turnCounter++;
                if (_turnCounter >= FocusSnapshotInterval)
                {
                    _turnCounter = 0;
                    await GenerateFocusSnapshotAsync(ev.CaseId, ev.Role, contextStore, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Curator processing failed for CaseId={CaseId}", ev.CaseId);
            }
        }
    }

    private async Task ProcessAsync(ConversationEvent ev, IContextStoreTool contextStore, CancellationToken ct)
    {
        // 1. Detect: 判断・意思決定を検出
        var recentText = string.Join("\n", ev.RecentTurns.Select(t => $"[{t.Speaker}] {t.Message}"));
        var detectMessages = new List<ChatMessage>
        {
            new(ChatRole.System, CuratorPrompts.BuildDetectSystemPrompt()),
            new(ChatRole.User, recentText),
        };
        var detectResponse = await chatClient.GetResponseAsync(detectMessages, cancellationToken: ct);
        var detectJson = detectResponse.Text?.Trim() ?? "[]";

        // 検出なし → 終了
        if (detectJson == "[]" || string.IsNullOrEmpty(detectJson)) return;

        // 2. Draft: 案件コンテキストを引いて判断ログを生成
        var entries = await contextStore.ReadCaseContextAsync(ev.CaseId, ev.Role, ct);
        var contextSummary = entries.Count > 0
            ? string.Join("\n", entries.Select(e => $"[{e.Role}/{e.Kind}] {e.Text}"))
            : "(コンテキストなし)";

        var draftMessages = new List<ChatMessage>
        {
            new(ChatRole.System, CuratorPrompts.BuildDraftSystemPrompt(ev.Role, contextSummary)),
            new(ChatRole.User, $"検出された判断: {detectJson}\n会話:\n{recentText}"),
        };
        var draftResponse = await chatClient.GetResponseAsync(draftMessages, cancellationToken: ct);
        var draftText = draftResponse.Text?.Trim() ?? "";

        // 3. Refine: 400字に圧縮
        var refineMessages = new List<ChatMessage>
        {
            new(ChatRole.System, "あなたは文章を簡潔にまとめるアシスタントです。"),
            new(ChatRole.User, draftText),
            new(ChatRole.Assistant, draftText),
            new(ChatRole.User, CuratorPrompts.BuildRefineInstruction()),
        };
        var refineResponse = await chatClient.GetResponseAsync(refineMessages, cancellationToken: ct);
        var finalText = refineResponse.Text?.Trim() ?? draftText;

        // 4. ContextStore に handoff として保存（全ロールから参照可能）
        await contextStore.AppendHandoffAsync(ev.CaseId, ev.Role, "Curator", finalText, ct);
        logger.LogInformation("Curator saved handoff for CaseId={CaseId}", ev.CaseId);
    }

    // FocusSnapshot 生成: 全エントリを取得し、指定ロール視点の要約 Markdown を LLM で生成して INSERT。
    // Curia FocusUpdateService の移植。
    private async Task GenerateFocusSnapshotAsync(
        string caseId, string role, IContextStoreTool contextStore, CancellationToken ct)
    {
        try
        {
            var allEntries = await contextStore.ReadCaseContextAsync(caseId, null, ct);
            if (allEntries.Count == 0)
            {
                logger.LogDebug("FocusSnapshot skipped (no entries): caseId={CaseId}", caseId);
                return;
            }

            var entriesText = string.Join("\n", allEntries.Select(
                e => $"[{e.CreatedAt:yyyy-MM-dd HH:mm}][{e.Role}/{e.Kind}] {e.Text}"));

            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, CuratorPrompts.BuildFocusSnapshotSystemPrompt(role)),
                new(ChatRole.User, entriesText),
            };

            var response = await chatClient.GetResponseAsync(messages, cancellationToken: ct);
            var summaryMd = response.Text?.Trim() ?? "";

            if (string.IsNullOrEmpty(summaryMd)) return;

            await contextStore.InsertFocusSnapshotAsync(caseId, role, summaryMd, ct);
            logger.LogInformation(
                "FocusSnapshot generated: caseId={CaseId} role={Role}", caseId, role);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "FocusSnapshot generation failed (non-fatal): caseId={CaseId} role={Role}",
                caseId, role);
        }
    }
}
