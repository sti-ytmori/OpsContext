using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Tools;

namespace OpsContext.Agents.Agents;

// design 04: OrchestratorAgent — ロール claim に応じて各ロールエージェントへ Handoff する。
// ロール翻訳プロンプト（plan.md「Orchestrator 翻訳プロンプト骨子」）を付加して呼び出す。
public class OrchestratorAgent
{
    private readonly IChatClient _chatClient;
    private readonly ISqlErpTool _sqlErp;
    private readonly IAiSearchTool _aiSearch;
    private readonly IContextStoreTool _contextStore;
    private readonly ILogger<OrchestratorAgent> _logger;

    // ロール別エージェント（Handoff 先）
    private readonly SalesAgent _salesAgent;
    private readonly AccountingAgent _accountingAgent;
    private readonly PurchasingAgent _purchasingAgent;
    private readonly ProductionAgent _productionAgent;

    public OrchestratorAgent(
        IChatClient chatClient,
        ISqlErpTool sqlErp,
        IAiSearchTool aiSearch,
        IContextStoreTool contextStore,
        ILogger<OrchestratorAgent> logger)
    {
        _chatClient = chatClient;
        _sqlErp = sqlErp;
        _aiSearch = aiSearch;
        _contextStore = contextStore;
        _logger = logger;

        _salesAgent = new SalesAgent(chatClient, sqlErp, aiSearch, contextStore,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SalesAgent>.Instance);
        _accountingAgent = new AccountingAgent(chatClient, sqlErp, aiSearch,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AccountingAgent>.Instance);
        _purchasingAgent = new PurchasingAgent(chatClient, sqlErp, aiSearch,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PurchasingAgent>.Instance);
        _productionAgent = new ProductionAgent(chatClient, sqlErp, aiSearch,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ProductionAgent>.Instance);
    }

    /// <summary>
    /// ユーザーメッセージを受け取り、ロール claim に応じたエージェントへ Handoff して応答を返す。
    /// Handoff 前に ReadCaseContextAsync でコンテキストを取得し contextSummary として渡す。
    /// </summary>
    public async Task<AgentResponse> HandleAsync(AgentRequest request, CancellationToken ct = default)
    {
        _logger.LogInformation("OrchestratorAgent.HandleAsync: CaseId={CaseId} Role={Role}",
            request.CaseId, request.Role);

        // コンテキスト取得（ロール視点でフィルタ）
        var contextEntries = await _contextStore.ReadCaseContextAsync(
            request.CaseId, request.Role, ct);

        var contextSummary = contextEntries.Count > 0
            ? string.Join("\n", contextEntries.Select(e =>
                $"[{e.CreatedAt:yyyy-MM-dd HH:mm}][{e.Kind}][{e.Role}] {e.Text}"))
            : "(コンテキストなし)";

        // ロール別 Handoff
        var handoffRequest = request with { ContextSummary = contextSummary };

        var response = request.Role switch
        {
            "Sales"      or "営業" => await _salesAgent.HandleAsync(handoffRequest, ct),
            "Accounting" or "経理" => await _accountingAgent.HandleAsync(handoffRequest, ct),
            "Purchasing" or "購買" => await _purchasingAgent.HandleAsync(handoffRequest, ct),
            "Production" or "生産" => await _productionAgent.HandleAsync(handoffRequest, ct),
            // Admin は全ロールを横断する管理者視点で Orchestrator が直接対応
            "Admin" => await HandleAdminRoleAsync(handoffRequest, ct),
            _ => await HandleUnknownRoleAsync(handoffRequest, ct)
        };

        _logger.LogInformation("OrchestratorAgent.HandleAsync: completed Role={Role} ToolCalls={ToolCallCount}",
            request.Role, response.ToolCalls.Count);

        return response;
    }

    // 全在庫サマリ取得 SQL（erp_inventory × erp_products JOIN）
    private const string AllInventorySql = """
        SELECT
            JSON_VALUE(i.CellsJson, '$.ProductCode')                              AS ProductCode,
            JSON_VALUE(p.CellsJson, '$.Name')                                     AS Name,
            JSON_VALUE(p.CellsJson, '$.Category')                                 AS Category,
            CAST(JSON_VALUE(p.CellsJson, '$.UnitPrice')    AS DECIMAL(18,0))      AS UnitPrice,
            CAST(JSON_VALUE(i.CellsJson, '$.OnHandQty')    AS INT)                AS OnHandQty,
            CAST(JSON_VALUE(i.CellsJson, '$.AllocatedQty') AS INT)                AS AllocatedQty,
            CAST(JSON_VALUE(i.CellsJson, '$.SafetyStock')  AS INT)                AS SafetyStock,
            CAST(JSON_VALUE(i.CellsJson, '$.OnHandQty') AS INT)
                - CAST(JSON_VALUE(i.CellsJson, '$.AllocatedQty') AS INT)          AS AvailableQty
        FROM GridRows i
        INNER JOIN GridRows p
            ON  p.TableId = 'erp_products'
            AND JSON_VALUE(p.CellsJson, '$.ProductCode') = JSON_VALUE(i.CellsJson, '$.ProductCode')
        WHERE i.TableId = 'erp_inventory'
        ORDER BY JSON_VALUE(i.CellsJson, '$.ProductCode')
        """;

    // 全顧客サマリ取得 SQL（erp_customers × erp_orders JOIN で与信使用額を集計）
    private const string AllCustomerSql = """
        SELECT
            JSON_VALUE(c.CellsJson, '$.CustomerCode')                               AS CustomerCode,
            JSON_VALUE(c.CellsJson, '$.Name')                                       AS Name,
            JSON_VALUE(c.CellsJson, '$.Industry')                                   AS Industry,
            CAST(JSON_VALUE(c.CellsJson, '$.CreditLimit')     AS DECIMAL(18,0))     AS CreditLimit,
            ISNULL(SUM(CAST(JSON_VALUE(o.CellsJson, '$.TotalAmount') AS DECIMAL(18,0))), 0)
                                                                                    AS UsedAmount,
            CAST(JSON_VALUE(c.CellsJson, '$.CreditLimit') AS DECIMAL(18,0))
                - ISNULL(SUM(CAST(JSON_VALUE(o.CellsJson, '$.TotalAmount') AS DECIMAL(18,0))), 0)
                                                                                    AS RemainingCredit,
            JSON_VALUE(c.CellsJson, '$.CreditRating')                               AS CreditRating
        FROM GridRows c
        LEFT JOIN GridRows o
            ON  o.TableId = 'erp_orders'
            AND JSON_VALUE(o.CellsJson, '$.CustomerCode') = JSON_VALUE(c.CellsJson, '$.CustomerCode')
            AND JSON_VALUE(o.CellsJson, '$.Status') IN ('Pending', 'Confirmed')
        WHERE c.TableId = 'erp_customers'
        GROUP BY c.CellsJson
        ORDER BY JSON_VALUE(c.CellsJson, '$.CustomerCode')
        """;

    // Admin ロール: 全在庫・全顧客サマリを RunReadOnlyQuery で取得し横断的に応答
    private async Task<AgentResponse> HandleAdminRoleAsync(AgentRequest request, CancellationToken ct)
    {
        var inventoryTask = _sqlErp.RunReadOnlyQueryAsync(AllInventorySql, ct);
        var customerTask  = _sqlErp.RunReadOnlyQueryAsync(AllCustomerSql, ct);
        await Task.WhenAll(inventoryTask, customerTask);

        var inventory = inventoryTask.Result;
        var customers  = customerTask.Result;

        var toolCalls = new List<ToolCallResult>
        {
            new("RunReadOnlyQuery(erp_inventory)", "全品番 現在庫", inventory.ToMarkdownTable()),
            new("RunReadOnlyQuery(erp_customers)", "全顧客 与信",   customers.ToMarkdownTable()),
        };

        var systemPrompt = AgentPromptComposer.Compose(
            BuildAdminSystemPrompt(),
            request.RolePrompt,
            request.PersonalPrompt);

        var userContent = $"""
            案件ID: {request.CaseId}
            コンテキスト:
            {request.ContextSummary}

            ## 全品番 現在庫サマリ
            {inventory.ToMarkdownTable()}

            ## 全顧客 与信サマリ
            {customers.ToMarkdownTable()}

            ユーザー質問: {request.UserMessage}
            """;

        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(AgentPromptComposer.ToHistoryMessages(request.History));
        messages.Add(new(ChatRole.User, userContent));

        var result = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return new AgentResponse(result.Text ?? "", toolCalls);
    }

    // 未知ロールは Orchestrator 自身が翻訳プロンプトで対応
    private async Task<AgentResponse> HandleUnknownRoleAsync(AgentRequest request, CancellationToken ct)
    {
        var systemPrompt = AgentPromptComposer.Compose(
            BuildOrchestratorSystemPrompt(request.Role),
            request.RolePrompt,
            request.PersonalPrompt);
        var userContent = $"""
            案件ID: {request.CaseId}
            コンテキスト:
            {request.ContextSummary}

            ユーザー質問: {request.UserMessage}
            """;

        var messages = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };
        messages.AddRange(AgentPromptComposer.ToHistoryMessages(request.History));
        messages.Add(new(ChatRole.User, userContent));

        var result = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
        return new AgentResponse(result.Text ?? "", []);
    }

    private static string BuildAdminSystemPrompt() => """
        あなたは全部門を管理するシステム管理者エージェントです。
        ユーザーの質問には、提供された在庫サマリ・顧客サマリのデータを使って具体的な数値で答えてください。
        データが提供されているにもかかわらず「条件を教えてください」とは聞かないこと。

        ## 役割と責務
        - 在庫・顧客与信データを横断的に参照して回答する
        - 営業・購買・生産・経理のいずれの視点でも分析できる
        - システム設定・アカウント管理・プロンプト設定に関する質問にも対応する

        ## 回答方針
        - 数値は必ず明示する（曖昧にしない）
        - 複数部門に影響する場合は部門ごとに整理して示す
        - 質問が「在庫」に関するものなら全品番の在庫テーブルを直接引用して答える
        - 質問が「顧客」に関するものなら全顧客の与信テーブルを直接引用して答える

        回答は日本語で、簡潔・具体的に行ってください。
        """;

    private static string BuildOrchestratorSystemPrompt(string role) => $"""
        あなたは業務エージェントのオーケストレーターです。

        ユーザーロール: {role}
        ロール別フォーカス: Sales=受注獲得 / Purchasing=仕入リスク / Production=生産枠 / Accounting=与信回収

        他ロール案件を見るときは {role} 観点で翻訳し、翻訳理由を1文添えてください。
        回答は日本語で、業務的に具体的・簡潔に行ってください。
        """;
}
