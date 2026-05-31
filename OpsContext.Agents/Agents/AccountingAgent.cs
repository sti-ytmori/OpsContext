using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Models;
using OpsContext.Agents.Tools;

namespace OpsContext.Agents.Agents;

// design 04: AccountingAgent — 与信・回収・売上計上影響を経理視点で分析するエージェント。
// GetCustomerCredit + SearchDecisionLogs を並列実行。
public sealed class AccountingAgent
{
    private readonly IChatClient _chatClient;
    private readonly ISqlErpTool _sqlErp;
    private readonly IAiSearchTool _aiSearch;
    private readonly ILogger<AccountingAgent> _logger;

    public AccountingAgent(
        IChatClient chatClient,
        ISqlErpTool sqlErp,
        IAiSearchTool aiSearch,
        ILogger<AccountingAgent> logger)
    {
        _chatClient = chatClient;
        _sqlErp = sqlErp;
        _aiSearch = aiSearch;
        _logger = logger;
    }

    public async Task<AgentResponse> HandleAsync(AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("AccountingAgent.HandleAsync: CaseId={CaseId}", request.CaseId);

        var customerCode = ExtractCustomerCode(request.UserMessage) ?? "A001";

        // ─── SQL + AI Search を並列実行 ────────────────────────────────────
        var creditTask = _sqlErp.GetCustomerCreditAsync(customerCode, ct);
        var searchTask = _aiSearch.SearchDecisionLogsAsync(
            request.UserMessage, customerCode, topK: 5, ct);

        await Task.WhenAll(creditTask, searchTask);

        var credit = creditTask.Result;
        var searchHits = searchTask.Result;

        // ─── ToolCallCard 生成 ────────────────────────────────────────────
        var toolCalls = new List<ToolCallResult>
        {
            new("GetCustomerCredit",
                $"customerCode={customerCode}",
                credit.ToMarkdownTable()),
            new("SearchDecisionLogs",
                $"query={request.UserMessage} customerCode={customerCode}",
                FormatSearchHits(searchHits))
        };

        // ─── LLM に経理視点の回答を生成させる ────────────────────────────
        var systemPrompt = AgentPromptComposer.Compose(
            BuildAccountingSystemPrompt(request.ContextSummary),
            request.RolePrompt,
            request.PersonalPrompt);
        var userContent = $"""
            ## ユーザー質問
            {request.UserMessage}

            ## 与信情報
            {credit.ToMarkdownTable()}

            ## 過去決定ログ・案件履歴
            {FormatSearchHits(searchHits)}
            """;

        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(AgentPromptComposer.ToHistoryMessages(request.History));
        messages.Add(new(ChatRole.User, userContent));

        var result = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var text = result.Text ?? "";

        _logger.LogInformation("AccountingAgent.HandleAsync: completed ToolCalls={Count}", toolCalls.Count);
        return new AgentResponse(text, toolCalls);
    }

    private static string BuildAccountingSystemPrompt(string? contextSummary) => $"""
        あなたは経理担当エージェントです。与信管理・売掛金回収・売上計上タイミングを最優先に分析してください。

        ## 役割と責務
        - 与信枠の使用状況と回収リスクの評価
        - 支払遅延履歴・与信超過リスクの警告
        - 売上計上が月を跨ぐ場合の影響評価
        - 経理承認フロー（大口受注1,000万円超）の必要性判断

        ## 回答方針
        - 与信残高・使用率は必ず数値で示す
        - リスクレベルを「高 / 中 / 低」で分類する
        - 経理承認が必要な場合は明示する

        ## 案件コンテキスト
        {contextSummary ?? "(なし)"}

        回答は日本語で、簡潔・具体的に行ってください。
        """;

    private static string FormatSearchHits(IReadOnlyList<AiSearchHit> hits)
    {
        if (hits.Count == 0) return "(関連情報なし)";
        return string.Join("\n\n", hits.Select((h, i) =>
            $"【ログ{i + 1}】スコア={h.Score:F3}\n{h.Content}"));
    }

    private static string? ExtractCustomerCode(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\b([A-Z]\d{3})\b");
        return match.Success ? match.Value : null;
    }
}
