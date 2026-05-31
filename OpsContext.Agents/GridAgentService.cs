using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Models;
using OpsContext.Agents.Tools;
using System.Text;
using System.Text.Json;

namespace OpsContext.Agents;

/// <summary>
/// 業務データ Grid に対する「自律ツール選択型」エージェントサービス。
///
/// 旧 GridChatService（LLM を1回呼んで固定アクション JSON を適用する意図分類器）を置換する。
/// 生の IChatClient に ChatOptions.Tools（AIFunction 群）を渡し、自前のツールループで
/// 1 ステップずつ実行する。FunctionInvokingChatClient ではラップしない:
///   - 実行過程を IProgress&lt;AgentActivity&gt; で UI にリアルタイム送出するため
///   - 書き込み系ツールを「即時実行せず提案キューに積む」承認ゲートを挟むため
///
/// ツールは2系統:
///   - 読み取り系（自動実行）: テーブル構造・行データ・基幹(ERP)を AI が自己判断で多段に読む
///   - 書き込み系（提案のみ）: Grid を変更せず PendingEdit を積む。適用は人間承認時のみ。
/// </summary>
public sealed class GridAgentService
{
    private readonly IChatClient _chatClient;
    private readonly ISqlErpTool _sqlErp;
    private readonly IAiSearchTool _aiSearch;
    private readonly IContextStoreTool _contextStore;
    private readonly IDataGridStore _gridStore;
    private readonly QuoteValidationService _validation;
    private readonly ILogger<GridAgentService> _logger;

    // システムプロンプトのマーカー（MockChatClient がツールモードを検出するために使う）
    internal const string SystemMarker = "GRID_AGENT_MODE";

    private const int MaxIterations = 10;

    public GridAgentService(
        IChatClient chatClient,
        ISqlErpTool sqlErp,
        IAiSearchTool aiSearch,
        IContextStoreTool contextStore,
        IDataGridStore gridStore,
        QuoteValidationService validation,
        ILogger<GridAgentService> logger)
    {
        _chatClient   = chatClient;
        _sqlErp       = sqlErp;
        _aiSearch     = aiSearch;
        _contextStore = contextStore;
        _gridStore    = gridStore;
        _validation   = validation;
        _logger       = logger;
    }

    // =======================================================================
    // メイン: 自律ツールループ
    // =======================================================================

    public async Task<GridAgentResult> HandleAsync(
        string caseId,
        string role,
        string userMessage,
        IProgress<AgentActivity>? progress,
        CancellationToken ct = default)
    {
        var table        = _gridStore.GetActiveTable();
        var pendingEdits = new List<PendingEdit>();
        var activities   = new List<AgentActivity>();
        var step         = 0;

        // ツール（AIFunction）を本リクエストのスコープで構築する。
        // 書き込み系は pendingEdits クロージャに積むだけで Grid を変更しない。
        var tools = BuildTools(caseId, pendingEdits, ct);
        var byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, BuildSystemPrompt(table)),
            new(ChatRole.User,   userMessage)
        };
        var options = new ChatOptions { Tools = [.. tools.Cast<AITool>()] };

        var finalText = "";

        try
        {
            for (var iter = 0; iter < MaxIterations; iter++)
            {
                var resp = await _chatClient.GetResponseAsync(messages, options, ct);
                messages.AddRange(resp.Messages);

                var calls = resp.Messages
                    .SelectMany(m => m.Contents)
                    .OfType<FunctionCallContent>()
                    .ToList();

                // ツール呼び出しが無ければ最終回答
                if (calls.Count == 0)
                {
                    finalText = resp.Text ?? "";
                    break;
                }

                // ツール呼び出しの直前に AI が添えた思考テキストを Reasoning として可視化
                var thinking = resp.Text;
                if (!string.IsNullOrWhiteSpace(thinking))
                {
                    var think = new AgentActivity
                    {
                        Step   = step++,
                        Kind   = ActivityKind.Reasoning,
                        Title  = thinking!.Length > 120 ? thinking[..120] + "…" : thinking,
                        Status = ActivityStatus.Done
                    };
                    activities.Add(think);
                    progress?.Report(think);
                }

                foreach (var fc in calls)
                {
                    var (kind, title) = DescribeCall(fc);
                    var activity = new AgentActivity
                    {
                        Step   = step++,
                        Kind   = kind,
                        Title  = title,
                        Detail = SummarizeArgs(fc.Arguments),
                        Status = ActivityStatus.Running
                    };
                    activities.Add(activity);
                    progress?.Report(activity);

                    string resultText;
                    try
                    {
                        if (byName.TryGetValue(fc.Name, out var fn))
                        {
                            var args = new AIFunctionArguments(fc.Arguments);
                            var raw  = await fn.InvokeAsync(args, ct);
                            resultText = raw?.ToString() ?? "";
                            activity.Status         = ActivityStatus.Done;
                            activity.ResultMarkdown = resultText;
                        }
                        else
                        {
                            resultText = $"(未知のツール: {fc.Name})";
                            activity.Status         = ActivityStatus.Failed;
                            activity.ResultMarkdown = resultText;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "GridAgentService: ツール実行失敗 {Tool}", fc.Name);
                        resultText = $"エラー: {ex.Message}";
                        activity.Status         = ActivityStatus.Failed;
                        activity.ResultMarkdown = resultText;
                    }
                    progress?.Report(activity);

                    messages.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(fc.CallId, resultText)]));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GridAgentService: ループ失敗");
            var err = new AgentActivity
            {
                Step   = step++,
                Kind   = ActivityKind.Error,
                Title  = $"エラーが発生しました: {ex.Message}",
                Status = ActivityStatus.Failed
            };
            activities.Add(err);
            progress?.Report(err);
            finalText = $"処理中にエラーが発生しました: {ex.Message}";
        }

        if (string.IsNullOrWhiteSpace(finalText))
            finalText = pendingEdits.Count > 0
                ? $"{pendingEdits.Count} 件の編集を提案しました。内容を確認のうえ承認してください。"
                : "ご指示を受け付けました。";

        var final = new AgentActivity
        {
            Step   = step,
            Kind   = ActivityKind.Final,
            Title  = "回答を生成しました",
            Status = ActivityStatus.Done
        };
        activities.Add(final);
        progress?.Report(final);

        return new GridAgentResult(finalText, activities, pendingEdits);
    }

    // =======================================================================
    // 承認時の適用（UI から呼ぶ）
    // =======================================================================

    /// <summary>承認された編集提案を IDataGridStore に適用し、observation を記録する。</summary>
    public async Task ApplyEditAsync(string caseId, string role, PendingEdit edit, CancellationToken ct = default)
    {
        var rowId = ResolveRowId(edit.RowId);

        switch (edit.Kind)
        {
            case PendingEditKind.SetCell when rowId is not null && edit.ColumnKey is not null:
                _gridStore.UpdateCell(rowId, edit.ColumnKey, edit.After);
                await Record(caseId, role,
                    $"セル更新: 行={edit.RowId} 列={edit.ColumnKey} 「{edit.Before}」→「{edit.After}」", ct);
                break;

            case PendingEditKind.AddRow:
                _gridStore.AddRow(edit.Cells);
                await Record(caseId, role, "行を追加しました。", ct);
                break;

            case PendingEditKind.AddColumn when edit.ColumnLabel is not null:
                _gridStore.AddColumn(new GridColumn(
                    MakeColumnKey(edit.ColumnLabel),
                    edit.ColumnLabel,
                    edit.ColumnType ?? GridColumnType.Text));
                await Record(caseId, role, $"列「{edit.ColumnLabel}」を追加しました。", ct);
                break;

            case PendingEditKind.DeleteRow when rowId is not null:
                _gridStore.DeleteRow(rowId);
                await Record(caseId, role, $"行={edit.RowId} を削除しました。", ct);
                break;

            case PendingEditKind.DeleteColumn when edit.ColumnKey is not null:
                _gridStore.DeleteColumn(edit.ColumnKey);
                await Record(caseId, role, $"列={edit.ColumnKey} を削除しました。", ct);
                break;

            case PendingEditKind.UpdateColumnDescription when edit.ColumnKey is not null:
                _gridStore.UpdateColumnDescription(edit.ColumnKey, edit.Description ?? "");
                await Record(caseId, role, $"列={edit.ColumnKey} の意味を更新しました。", ct);
                break;
        }
    }

    // =======================================================================
    // ツール定義（AIFunction）
    // =======================================================================

    private List<AIFunction> BuildTools(
        string caseId, List<PendingEdit> pendingEdits, CancellationToken ct)
    {
        // ── 読み取り系（自動実行） ──────────────────────────────────────────
        var describeTable = AIFunctionFactory.Create(
            () => DescribeTable(),
            "describe_table",
            "現在の業務データテーブルの構造を返す。列キー・表示名・型・列の意味(説明)・行数を含む。" +
            "まずこれを呼んでデータ構造と各列の意味を理解すること。");

        var readRows = AIFunctionFactory.Create(
            (int? limit, string? contains) => ReadRows(limit, contains),
            "read_rows",
            "業務データの行を返す。limit で件数制限、contains で文字列を含む行に絞り込める。" +
            "各行には rowId（先頭8文字）が付き、セル編集の提案で参照する。");

        var getCredit = AIFunctionFactory.Create(
            async (string customerCode) =>
                (await _sqlErp.GetCustomerCreditAsync(customerCode, ct)).ToMarkdownTable(),
            "get_customer_credit",
            "基幹システムから顧客の与信枠・使用額・残額・与信格付けを照会する。");

        var getInventory = AIFunctionFactory.Create(
            async (string productCode) =>
                (await _sqlErp.GetProductInventoryAsync(productCode, ct)).ToMarkdownTable(),
            "get_product_inventory",
            "基幹システムから製品の在庫（手持・引当・安全在庫・有効在庫）を照会する。");

        var getCapacity = AIFunctionFactory.Create(
            async (string productCode, string? from, string? to) =>
            {
                var f = ParseDate(from) ?? DateOnly.FromDateTime(DateTime.Today);
                var t = ParseDate(to)   ?? f.AddDays(14);
                return (await _sqlErp.GetProductionCapacityAsync(productCode, f, t, ct)).ToMarkdownTable();
            },
            "get_production_capacity",
            "基幹システムから製品の生産能力（日別、末尾に合計）を照会する。from/to は yyyy-MM-dd。");

        var similarOrders = AIFunctionFactory.Create(
            async (string customerCode, string productCode) =>
                (await _sqlErp.SearchSimilarOrdersAsync(customerCode, productCode, 5, ct)).ToMarkdownTable(),
            "search_similar_orders",
            "基幹システムから同一顧客・製品の過去受注を照会する。");

        var searchKnowledge = AIFunctionFactory.Create(
            async (string query) =>
            {
                var hits = await _aiSearch.SearchKnowledgeAsync(query, 5, null, ct);
                return hits.Count == 0
                    ? "(関連ナレッジなし)"
                    : string.Join("\n\n", hits.Select((h, i) => $"【{i + 1}】score={h.Score:F3}\n{h.Content}"));
            },
            "search_knowledge",
            "ナレッジベース（社内手順・過去決定ログ）を全文/ベクトル検索する。");

        var evaluateQuote = AIFunctionFactory.Create(
            async () => await EvaluateQuoteAsync(caseId, ct),
            "evaluate_quote_against_erp",
            "現在の見積明細を基幹データと突合し、各行の判定(OK/Warning/NG)・リスク・推奨アクションを" +
            "決定的に計算して返す。数値判断はこのツールに任せ、結果を propose_set_cell で書き込み提案すること。");

        // ── 書き込み系（提案のみ・Grid 非変更） ─────────────────────────────
        var proposeSetCell = AIFunctionFactory.Create(
            (string rowId, string columnKey, string value) =>
            {
                var before = ResolveCellValue(rowId, columnKey);
                pendingEdits.Add(new PendingEdit
                {
                    Kind      = PendingEditKind.SetCell,
                    RowId     = rowId,
                    ColumnKey = columnKey,
                    Before    = before,
                    After     = value,
                    Summary   = $"行 {rowId} の {ColumnLabel(columnKey)} を「{before}」→「{value}」"
                });
                return "セル更新を提案しました（人間の承認待ち）。";
            },
            "propose_set_cell",
            "セル値の更新を提案する。即時には変更されず、人間が承認したときだけ適用される。" +
            "rowId は read_rows が返した先頭8文字でよい。");

        var proposeAddRow = AIFunctionFactory.Create(
            (string cellsJson) =>
            {
                var cells = ParseCells(cellsJson);
                pendingEdits.Add(new PendingEdit
                {
                    Kind    = PendingEditKind.AddRow,
                    Cells   = cells,
                    After   = cellsJson,
                    Summary = "行を追加: " + string.Join(", ", (cells ?? []).Select(kv => $"{kv.Key}={kv.Value}"))
                });
                return "行追加を提案しました（人間の承認待ち）。";
            },
            "propose_add_row",
            "新規行の追加を提案する。cellsJson は {\"列キー\":\"値\", ...} の JSON 文字列。");

        var proposeAddColumn = AIFunctionFactory.Create(
            (string label, string columnType) =>
            {
                pendingEdits.Add(new PendingEdit
                {
                    Kind        = PendingEditKind.AddColumn,
                    ColumnLabel = label,
                    ColumnType  = ParseColumnType(columnType),
                    After       = $"{label} ({columnType})",
                    Summary     = $"列「{label}」({columnType}) を追加"
                });
                return "列追加を提案しました（人間の承認待ち）。";
            },
            "propose_add_column",
            "新規列の追加を提案する。columnType は Text / Number / Date のいずれか。");

        var proposeDeleteRow = AIFunctionFactory.Create(
            (string rowId) =>
            {
                pendingEdits.Add(new PendingEdit
                {
                    Kind    = PendingEditKind.DeleteRow,
                    RowId   = rowId,
                    Before  = ResolveRowSummary(rowId),
                    Summary = $"行 {rowId} を削除"
                });
                return "行削除を提案しました（人間の承認待ち）。";
            },
            "propose_delete_row",
            "行の削除を提案する。rowId は read_rows が返した先頭8文字でよい。");

        var proposeDeleteColumn = AIFunctionFactory.Create(
            (string columnKey) =>
            {
                pendingEdits.Add(new PendingEdit
                {
                    Kind      = PendingEditKind.DeleteColumn,
                    ColumnKey = columnKey,
                    Before    = ColumnLabel(columnKey),
                    Summary   = $"列「{ColumnLabel(columnKey)}」を削除"
                });
                return "列削除を提案しました（人間の承認待ち）。";
            },
            "propose_delete_column",
            "列の削除を提案する。");

        return
        [
            describeTable, readRows, getCredit, getInventory, getCapacity,
            similarOrders, searchKnowledge, evaluateQuote,
            proposeSetCell, proposeAddRow, proposeAddColumn, proposeDeleteRow, proposeDeleteColumn
        ];
    }

    // =======================================================================
    // ツール実装ヘルパ（読み取り）
    // =======================================================================

    private string DescribeTable()
    {
        var table = _gridStore.GetActiveTable();
        var sb = new StringBuilder();
        sb.AppendLine($"テーブル名: {table.Name}  行数: {table.Rows.Count}");
        sb.AppendLine("列定義:");
        foreach (var col in table.Columns)
            sb.AppendLine($"- key={col.Key} / 表示名={col.Label} / 型={col.Type} / 意味={(string.IsNullOrWhiteSpace(col.Description) ? "(未設定)" : col.Description)}");
        return sb.ToString();
    }

    private string ReadRows(int? limit, string? contains)
    {
        var table = _gridStore.GetActiveTable();
        IEnumerable<GridRow> rows = table.Rows;

        if (!string.IsNullOrWhiteSpace(contains))
            rows = rows.Where(r => r.Cells.Values.Any(v =>
                v?.ToString()?.Contains(contains, StringComparison.OrdinalIgnoreCase) == true));

        if (limit is > 0)
            rows = rows.Take(limit.Value);

        var list = rows.ToList();
        if (list.Count == 0) return "(該当行なし)";

        var sb = new StringBuilder();
        sb.Append("| rowId | ").Append(string.Join(" | ", table.Columns.Select(c => c.Label))).AppendLine(" |");
        sb.Append("| --- |").Append(string.Concat(Enumerable.Repeat(" --- |", table.Columns.Count))).AppendLine();
        foreach (var row in list)
        {
            sb.Append("| ").Append(row.RowId[..8]).Append(" | ");
            sb.Append(string.Join(" | ", table.Columns.Select(c =>
                row.Cells.TryGetValue(c.Key, out var v) ? v?.ToString() ?? "" : "")));
            sb.AppendLine(" |");
        }
        return sb.ToString();
    }

    private async Task<string> EvaluateQuoteAsync(string caseId, CancellationToken ct)
    {
        var table = _gridStore.GetActiveTable();

        // 列キーをヒューリスティックで解決
        string Find(params string[] hints)
        {
            foreach (var h in hints)
            {
                var k = table.Columns.FirstOrDefault(c =>
                    c.Key.Contains(h, StringComparison.OrdinalIgnoreCase));
                if (k != null) return k.Key;
            }
            return "";
        }

        var kCode  = Find("product_code", "code", "品番");
        var kName  = Find("product_name", "name", "品名");
        var kQty   = Find("qty", "quantity", "数量");
        var kPrice = Find("unit_price", "price", "単価");
        var kDate  = Find("req_date", "date", "納期");

        var lines = new List<QuoteLine>();
        foreach (var row in table.Rows)
        {
            var code = Cell(row, kCode);
            if (string.IsNullOrEmpty(code)) continue;
            lines.Add(new QuoteLine
            {
                ProductCode   = code,
                ProductName   = Cell(row, kName),
                Qty           = int.TryParse(Cell(row, kQty), out var q) ? q : 0,
                UnitPrice     = decimal.TryParse(Cell(row, kPrice), out var p) ? p : 0m,
                RequestedDate = DateOnly.TryParse(Cell(row, kDate), out var d)
                                    ? d : DateOnly.FromDateTime(DateTime.Today.AddDays(30)),
            });
        }

        if (lines.Count == 0)
            return "品番列が見つからないため突合できません。describe_table で列構造を確認してください。";

        var customerCode  = "A001"; // デモ既定顧客
        var requestedDate = lines.Select(l => l.RequestedDate).Min();
        var (validated, _) = await _validation.ValidateAsync(
            lines.AsReadOnly(), caseId, customerCode, requestedDate, ct);

        var sb = new StringBuilder();
        sb.AppendLine("| 品番 | 判定 | リスク | 推奨アクション | 参照 |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var v in validated)
            sb.AppendLine($"| {v.ProductCode} | {v.Verdict} | {v.RiskLevel} | {v.Recommendation} | {v.RefNote} |");
        sb.AppendLine();
        sb.AppendLine($"対象列: 品番={kCode} / 判定列の書き込み先候補= {Find("verdict", "判定")} / リスク={Find("risk", "リスク")} / 推奨={Find("recommend", "推奨")}");
        return sb.ToString();
    }

    // =======================================================================
    // システムプロンプト
    // =======================================================================

    private static string BuildSystemPrompt(GridTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {SystemMarker}");
        sb.AppendLine("あなたは業務データ Grid を扱う自律エージェントです。ツールを自分で選んで段階的に課題を解決します。");
        sb.AppendLine();
        sb.AppendLine("## 行動規範");
        sb.AppendLine("- まず describe_table と read_rows でデータ構造・各列の意味・中身を理解してから動くこと。");
        sb.AppendLine("- 列の意味(説明)を踏まえ、どの基幹照会(get_customer_credit / get_product_inventory / get_production_capacity / search_similar_orders / evaluate_quote_against_erp)が必要かを自分で判断して呼ぶこと。");
        sb.AppendLine("- 数値判断は evaluate_quote_against_erp に任せ、その結果を根拠にすること。");
        sb.AppendLine("- データ変更は必ず propose_* ツールで提案すること。あなたは Grid を直接変更できない。承認は人間が行う。");
        sb.AppendLine("- 「更新しました」と断定せず、「〜を提案しました（承認待ち）」と述べること。");
        sb.AppendLine("- 最終回答は日本語で、何を読み・何を根拠に・何を提案したかを簡潔にまとめること。");
        sb.AppendLine();
        sb.AppendLine($"## 現在のテーブル「{table.Name}」の列");
        foreach (var col in table.Columns)
            sb.AppendLine($"- key={col.Key} 表示名={col.Label} 型={col.Type} 意味={(string.IsNullOrWhiteSpace(col.Description) ? "(未設定)" : col.Description)}");
        sb.AppendLine($"（行数: {table.Rows.Count}。具体的な行は read_rows で取得すること）");
        return sb.ToString();
    }

    // =======================================================================
    // 表示・解決ヘルパ
    // =======================================================================

    private static (ActivityKind Kind, string Title) DescribeCall(FunctionCallContent fc)
    {
        var arg = fc.Arguments;
        string A(string key) => arg is not null && arg.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
        return fc.Name switch
        {
            "describe_table"             => (ActivityKind.ToolCall, "テーブル構造を確認"),
            "read_rows"                  => (ActivityKind.ToolCall, "行データを読み取り"),
            "get_customer_credit"        => (ActivityKind.ToolCall, $"与信を照会 ({A("customerCode")})"),
            "get_product_inventory"      => (ActivityKind.ToolCall, $"在庫を照会 ({A("productCode")})"),
            "get_production_capacity"    => (ActivityKind.ToolCall, $"生産枠を照会 ({A("productCode")})"),
            "search_similar_orders"      => (ActivityKind.ToolCall, "過去受注を照会"),
            "search_knowledge"           => (ActivityKind.ToolCall, "ナレッジを検索"),
            "evaluate_quote_against_erp" => (ActivityKind.ToolCall, "基幹データと突合・判定"),
            "propose_set_cell"           => (ActivityKind.Proposal, $"セル更新を提案 ({A("columnKey")})"),
            "propose_add_row"            => (ActivityKind.Proposal, "行追加を提案"),
            "propose_add_column"         => (ActivityKind.Proposal, $"列追加を提案 ({A("label")})"),
            "propose_delete_row"         => (ActivityKind.Proposal, "行削除を提案"),
            "propose_delete_column"      => (ActivityKind.Proposal, "列削除を提案"),
            _                            => (ActivityKind.ToolCall, fc.Name)
        };
    }

    private static string? SummarizeArgs(IDictionary<string, object?>? args)
    {
        if (args is null || args.Count == 0) return null;
        return string.Join(", ", args.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    private string ColumnLabel(string columnKey)
    {
        var col = _gridStore.GetActiveTable().Columns.FirstOrDefault(c => c.Key == columnKey);
        return col?.Label ?? columnKey;
    }

    private string? ResolveCellValue(string rowId, string columnKey)
    {
        var full = ResolveRowId(rowId);
        if (full is null) return null;
        var row = _gridStore.GetActiveTable().Rows.FirstOrDefault(r => r.RowId == full);
        return row is not null && row.Cells.TryGetValue(columnKey, out var v) ? v?.ToString() : null;
    }

    private string? ResolveRowSummary(string rowId)
    {
        var full = ResolveRowId(rowId);
        if (full is null) return null;
        var row = _gridStore.GetActiveTable().Rows.FirstOrDefault(r => r.RowId == full);
        return row is null ? null : string.Join(" / ", row.Cells.Values.Take(3).Select(v => v?.ToString() ?? ""));
    }

    /// <summary>先頭8文字のショート ID でも完全 ID を解決する。</summary>
    private string? ResolveRowId(string? rowId)
    {
        if (string.IsNullOrEmpty(rowId)) return null;
        var rows = _gridStore.GetActiveTable().Rows;
        var row  = rows.FirstOrDefault(r => r.RowId == rowId)
                ?? rows.FirstOrDefault(r => r.RowId.StartsWith(rowId, StringComparison.Ordinal));
        return row?.RowId;
    }

    private async Task Record(string caseId, string role, string text, CancellationToken ct)
    {
        try
        {
            await _contextStore.AppendObservationAsync(caseId, role, "GridAgentService", text, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GridAgentService: observation 記録失敗");
        }
    }

    private static string Cell(GridRow row, string key)
        => !string.IsNullOrEmpty(key) && row.Cells.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    private static GridColumnType ParseColumnType(string? type) => type switch
    {
        "Number" => GridColumnType.Number,
        "Date"   => GridColumnType.Date,
        _        => GridColumnType.Text
    };

    private static string MakeColumnKey(string label)
        => label.ToLowerInvariant().Replace(" ", "_").Replace("　", "_").Replace("（", "").Replace("）", "");

    private static DateOnly? ParseDate(string? s)
        => DateOnly.TryParse(s, out var d) ? d : null;

    private static Dictionary<string, object?>? ParseCells(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;
            return doc.RootElement.EnumerateObject()
                .ToDictionary(p => p.Name, p => (object?)p.Value.ToString());
        }
        catch
        {
            return null;
        }
    }
}
