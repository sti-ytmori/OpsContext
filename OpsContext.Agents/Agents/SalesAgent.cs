using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Models;
using OpsContext.Agents.Tools;

namespace OpsContext.Agents.Agents;

// design 04: SalesAgent — 営業視点で受注獲得・粗利・顧客リスクを分析するエージェント。
// SQL 3本（GetCustomerCredit / GetProductInventory / GetProductionCapacity）+
// SearchDecisionLogs を Task.WhenAll で並列実行するのが plan.md のメインデモシナリオの肝。
public sealed class SalesAgent
{
    private readonly IChatClient _chatClient;
    private readonly ISqlErpTool _sqlErp;
    private readonly IAiSearchTool _aiSearch;
    private readonly IContextStoreTool _contextStore;
    private readonly ILogger<SalesAgent> _logger;

    public SalesAgent(
        IChatClient chatClient,
        ISqlErpTool sqlErp,
        IAiSearchTool aiSearch,
        IContextStoreTool contextStore,
        ILogger<SalesAgent> logger)
    {
        _chatClient = chatClient;
        _sqlErp = sqlErp;
        _aiSearch = aiSearch;
        _contextStore = contextStore;
        _logger = logger;
    }

    public async Task<AgentResponse> HandleAsync(AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("SalesAgent.HandleAsync: CaseId={CaseId}", request.CaseId);

        // メッセージから顧客コードと製品コードを簡易抽出（デモ用: A001 / 弁P-101 をデフォルト）
        var customerCode = ExtractCustomerCode(request.UserMessage) ?? "A001";
        var productCode = ErpReferenceData.NormalizeProductCode(
            ExtractProductCode(request.UserMessage) ?? "P-101");
        var today = DateOnly.FromDateTime(DateTime.Today);
        var twoWeeksLater = today.AddDays(14);

        // ─── SQL 3本 + AI Search を並列実行 ─────────────────────────────────
        var creditTask = _sqlErp.GetCustomerCreditAsync(customerCode, ct);
        var inventoryTask = _sqlErp.GetProductInventoryAsync(productCode, ct);
        var capacityTask = _sqlErp.GetProductionCapacityAsync(productCode, today, twoWeeksLater, ct);
        var searchTask = _aiSearch.SearchDecisionLogsAsync(request.UserMessage, customerCode, topK: 5, ct);

        await Task.WhenAll(creditTask, inventoryTask, capacityTask, searchTask);

        var credit = creditTask.Result;
        var inventory = inventoryTask.Result;
        var capacity = capacityTask.Result;
        var searchHits = searchTask.Result;

        // ─── ToolCallCard 生成 ────────────────────────────────────────────────
        var toolCalls = new List<ToolCallResult>
        {
            new("GetCustomerCredit",
                $"customerCode={customerCode}",
                credit.ToMarkdownTable()),
            new("GetProductInventory",
                $"productCode={productCode}",
                inventory.ToMarkdownTable()),
            new("GetProductionCapacity",
                $"productCode={productCode} from={today} to={twoWeeksLater}",
                capacity.ToMarkdownTable()),
            new("SearchDecisionLogs",
                $"query={request.UserMessage} customerCode={customerCode}",
                FormatSearchHits(searchHits))
        };

        // ─── LLM に営業視点の回答を生成させる ────────────────────────────────
        var systemPrompt = AgentPromptComposer.Compose(
            BuildSalesSystemPrompt(request.ContextSummary),
            request.RolePrompt,
            request.PersonalPrompt);
        var userContent = $"""
            ## ユーザー質問
            {request.UserMessage}

            ## 与信情報
            {credit.ToMarkdownTable()}

            ## 在庫情報
            {inventory.ToMarkdownTable()}

            ## 生産能力（今後2週間）
            {capacity.ToMarkdownTable()}

            ## 過去類似案件・決定ログ
            {FormatSearchHits(searchHits)}
            """;

        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(AgentPromptComposer.ToHistoryMessages(request.History));
        messages.Add(new(ChatRole.User, userContent));

        var result = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        var text = result.Text ?? "";

        _logger.LogInformation("SalesAgent.HandleAsync: completed ToolCalls={Count}", toolCalls.Count);
        return new AgentResponse(text, toolCalls);
    }

    private static string BuildSalesSystemPrompt(string? contextSummary) => $"""
        あなたは営業担当エージェントです。受注獲得・粗利確保・顧客との長期関係維持を最優先に分析してください。

        ## 役割と責務
        - 与信枠の状況を踏まえた受注可否の判断
        - 在庫・生産能力と納期の整合性チェック
        - 分割受注・代替提案などの受注獲得シナリオの提示
        - 過去類似案件を参照した判断根拠の明示

        ## 回答方針
        - 数値は必ず明示する（曖昧にしない）
        - リスクがある場合は「NG / Warning / OK」で判定してから説明する
        - 推奨アクションを最後に箇条書きで示す

        ## 案件コンテキスト
        {contextSummary ?? "(なし)"}

        回答は日本語で、簡潔・具体的に行ってください。
        """;

    private static string FormatSearchHits(IReadOnlyList<AiSearchHit> hits)
    {
        if (hits.Count == 0) return "(関連情報なし)";
        return string.Join("\n\n", hits.Select((h, i) =>
            $"【事例{i + 1}】スコア={h.Score:F3}\n{h.Content}"));
    }

    // 簡易抽出: メッセージ内の顧客コードパターン（A001 等）を検出
    private static string? ExtractCustomerCode(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(message, @"\b([A-Z]\d{3})\b");
        return match.Success ? match.Value : null;
    }

    // 簡易抽出: 製品コードパターン（弁P-101 等）を検出
    private static string? ExtractProductCode(string message)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            message, @"[弁ポンプフラ\w]+-\d+");
        return match.Success ? match.Value : null;
    }
}
