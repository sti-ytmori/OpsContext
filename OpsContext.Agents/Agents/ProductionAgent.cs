using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Models;
using OpsContext.Agents.Tools;

namespace OpsContext.Agents.Agents;

// design 04: ProductionAgent — 生産枠・スケジュールを生産管理視点で分析するエージェント。
// GetProductionCapacity + GetProductInventory を並列実行。
public sealed class ProductionAgent
{
    private readonly IChatClient _chatClient;
    private readonly ISqlErpTool _sqlErp;
    private readonly IAiSearchTool _aiSearch;
    private readonly ILogger<ProductionAgent> _logger;

    public ProductionAgent(
        IChatClient chatClient,
        ISqlErpTool sqlErp,
        IAiSearchTool aiSearch,
        ILogger<ProductionAgent> logger)
    {
        _chatClient = chatClient;
        _sqlErp = sqlErp;
        _aiSearch = aiSearch;
        _logger = logger;
    }

    public async Task<AgentResponse> HandleAsync(AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("ProductionAgent.HandleAsync: CaseId={CaseId}", request.CaseId);

        var productCode = ErpReferenceData.NormalizeProductCode(
            ExtractProductCode(request.UserMessage) ?? "P-101");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var twoWeeksLater = today.AddDays(14);

        // ─── SQL 2本 + AI Search を並列実行 ──────────────────────────────────
        var capacityTask = _sqlErp.GetProductionCapacityAsync(productCode, today, twoWeeksLater, ct);
        var inventoryTask = _sqlErp.GetProductInventoryAsync(productCode, ct);
        var searchTask = _aiSearch.SearchKnowledgeAsync(
            $"生産 製造 {productCode} 工程 リードタイム 能力", topK: 3, roleFilter: "Production", ct);

        await Task.WhenAll(capacityTask, inventoryTask, searchTask);

        var capacity = capacityTask.Result;
        var inventory = inventoryTask.Result;
        var knowledgeHits = searchTask.Result;

        // ─── ToolCallCard 生成 ────────────────────────────────────────────────
        var toolCalls = new List<ToolCallResult>
        {
            new("GetProductionCapacity",
                $"productCode={productCode} from={today:yyyy-MM-dd} to={twoWeeksLater:yyyy-MM-dd}",
                capacity.ToMarkdownTable()),
            new("GetProductInventory",
                $"productCode={productCode}",
                inventory.ToMarkdownTable()),
            new("SearchKnowledge",
                $"生産 製造 {productCode} 工程 リードタイム",
                FormatSearchHits(knowledgeHits))
        };

        // ─── LLM に生産視点の回答を生成させる ────────────────────────────────
        var systemPrompt = AgentPromptComposer.Compose(
            BuildProductionSystemPrompt(request.ContextSummary),
            request.RolePrompt,
            request.PersonalPrompt);
        var userContent = $"""
            ## ユーザー質問
            {request.UserMessage}

            ## 生産能力（今日から2週間）
            {capacity.ToMarkdownTable()}

            ## 在庫情報
            {inventory.ToMarkdownTable()}

            ## 生産ナレッジ
            {FormatSearchHits(knowledgeHits)}
            """;

        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(AgentPromptComposer.ToHistoryMessages(request.History));
        messages.Add(new(ChatRole.User, userContent));

        var result = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var text = result.Text ?? "";

        _logger.LogInformation("ProductionAgent.HandleAsync: completed ToolCalls={Count}", toolCalls.Count);
        return new AgentResponse(text, toolCalls);
    }

    private static string BuildProductionSystemPrompt(string? contextSummary) => $"""
        あなたは生産管理担当エージェントです。生産能力・スケジュール・工程リードタイムを最優先に分析してください。

        ## 役割と責務
        - 2週間の生産能力と要求数量の突合
        - 生産枠の逼迫・余剰の評価
        - 手持在庫 + 生産枠の合計で納期実現可否の判断
        - 分割納品・優先生産の実現性検討
        - 生産スケジュールへの影響度評価

        ## 回答方針
        - 生産可能数・納期は必ず数値と日付で示す
        - 生産リスクを「高 / 中 / 低」で分類する
        - 納期実現が困難な場合は代替案（分割納品・優先枠確保）を提示する
        - 在庫 + 生産枠の合計を明示する

        ## 案件コンテキスト
        {contextSummary ?? "(なし)"}

        回答は日本語で、簡潔・具体的に行ってください。
        """;

    private static string FormatSearchHits(IReadOnlyList<AiSearchHit> hits)
    {
        if (hits.Count == 0) return "(関連情報なし)";
        return string.Join("\n\n", hits.Select((h, i) =>
            $"【ナレッジ{i + 1}】スコア={h.Score:F3}\n{h.Content}"));
    }

    private static string? ExtractProductCode(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"[a-zA-Zぁ-鿿]{1,4}[P\-]?\d{2,5}");
        return match.Success ? match.Value : null;
    }
}
