using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Models;
using OpsContext.Agents.Tools;

namespace OpsContext.Agents.Agents;

// design 04: PurchasingAgent — 在庫・調達リスクを購買視点で分析するエージェント。
// GetProductInventory + SearchSimilarOrders を並列実行。
public sealed class PurchasingAgent
{
    private readonly IChatClient _chatClient;
    private readonly ISqlErpTool _sqlErp;
    private readonly IAiSearchTool _aiSearch;
    private readonly ILogger<PurchasingAgent> _logger;

    public PurchasingAgent(
        IChatClient chatClient,
        ISqlErpTool sqlErp,
        IAiSearchTool aiSearch,
        ILogger<PurchasingAgent> logger)
    {
        _chatClient = chatClient;
        _sqlErp = sqlErp;
        _aiSearch = aiSearch;
        _logger = logger;
    }

    public async Task<AgentResponse> HandleAsync(AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("PurchasingAgent.HandleAsync: CaseId={CaseId}", request.CaseId);

        var productCode = ErpReferenceData.NormalizeProductCode(
            ExtractProductCode(request.UserMessage) ?? "P-101");
        var customerCode = ExtractCustomerCode(request.UserMessage) ?? "A001";

        // ─── SQL 2本 + AI Search を並列実行 ──────────────────────────────────
        var inventoryTask = _sqlErp.GetProductInventoryAsync(productCode, ct);
        var similarOrdersTask = _sqlErp.SearchSimilarOrdersAsync(customerCode, productCode, topN: 5, ct);
        var searchTask = _aiSearch.SearchKnowledgeAsync(
            $"購買 調達 {productCode} リードタイム 代替品", topK: 3, roleFilter: "Purchasing", ct);

        await Task.WhenAll(inventoryTask, similarOrdersTask, searchTask);

        var inventory = inventoryTask.Result;
        var similarOrders = similarOrdersTask.Result;
        var knowledgeHits = searchTask.Result;

        // ─── ToolCallCard 生成 ────────────────────────────────────────────────
        var toolCalls = new List<ToolCallResult>
        {
            new("GetProductInventory",
                $"productCode={productCode}",
                inventory.ToMarkdownTable()),
            new("SearchSimilarOrders",
                $"customerCode={customerCode} productCode={productCode} topN=5",
                similarOrders.ToMarkdownTable()),
            new("SearchKnowledge",
                $"購買 調達 {productCode} リードタイム",
                FormatSearchHits(knowledgeHits))
        };

        // ─── LLM に購買視点の回答を生成させる ────────────────────────────────
        var systemPrompt = AgentPromptComposer.Compose(
            BuildPurchasingSystemPrompt(request.ContextSummary),
            request.RolePrompt,
            request.PersonalPrompt);
        var userContent = $"""
            ## ユーザー質問
            {request.UserMessage}

            ## 在庫情報
            {inventory.ToMarkdownTable()}

            ## 類似受注履歴（過去5件）
            {similarOrders.ToMarkdownTable()}

            ## 購買ナレッジ
            {FormatSearchHits(knowledgeHits)}
            """;

        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(AgentPromptComposer.ToHistoryMessages(request.History));
        messages.Add(new(ChatRole.User, userContent));

        var result = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var text = result.Text ?? "";

        _logger.LogInformation("PurchasingAgent.HandleAsync: completed ToolCalls={Count}", toolCalls.Count);
        return new AgentResponse(text, toolCalls);
    }

    private static string BuildPurchasingSystemPrompt(string? contextSummary) => $"""
        あなたは購買担当エージェントです。在庫リスク・調達リードタイム・代替品調達の観点を最優先に分析してください。

        ## 役割と責務
        - 手持在庫と有効在庫の過不足評価
        - 発注数量に対するリードタイム・調達可能性の判断
        - 過去受注履歴から調達パターンの把握
        - 安全在庫割れリスクと緊急調達の必要性判断
        - 代替品・複数ベンダー調達の提案

        ## 回答方針
        - 在庫数・リードタイムは必ず数値で示す
        - 調達リスクを「高 / 中 / 低」で分類する
        - 緊急調達が必要な場合は明示する
        - 過去受注の数量・頻度から需要パターンを読み取る

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

    private static string? ExtractCustomerCode(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\b([A-Z]\d{3})\b");
        return match.Success ? match.Value : null;
    }
}
