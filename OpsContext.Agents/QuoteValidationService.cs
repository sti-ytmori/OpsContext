using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpsContext.Agents.Agents;
using OpsContext.Agents.Models;
using OpsContext.Agents.Tools;

namespace OpsContext.Agents;

// design 08: QuoteValidationService — 見積明細を基幹データと突合して判定する。
// 数値判断はコード（ICalcTool）で行い、LLM は推奨文生成のみを担当する。
public sealed class QuoteValidationService
{
    private readonly ISqlErpTool _sqlErp;
    private readonly ICalcTool _calc;
    private readonly IChatClient _chatClient;
    private readonly IContextStoreTool _contextStore;
    private readonly ILogger<QuoteValidationService> _logger;

    public QuoteValidationService(
        ISqlErpTool sqlErp,
        ICalcTool calc,
        IChatClient chatClient,
        IContextStoreTool contextStore,
        ILogger<QuoteValidationService> logger)
    {
        _sqlErp = sqlErp;
        _calc = calc;
        _chatClient = chatClient;
        _contextStore = contextStore;
        _logger = logger;
    }

    /// <summary>
    /// 見積明細リストを基幹データと突合し、各行に Verdict / RiskLevel / Recommendation / RefNote を付与する。
    /// </summary>
    /// <returns>(判定済み明細リスト, ToolCallCard リスト)</returns>
    public async Task<(IReadOnlyList<QuoteLine> Lines, IReadOnlyList<ToolCallResult> ToolCalls)> ValidateAsync(
        IReadOnlyList<QuoteLine> lines,
        string caseId,
        string customerCode,
        DateOnly requestedDate,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "QuoteValidationService.ValidateAsync: caseId={CaseId} customerCode={CustomerCode} lines={Count}",
            caseId, customerCode, lines.Count);

        var toolCalls = new List<ToolCallResult>();

        // ─── 1. distinct 製品コードで在庫・生産枠と与信を並列取得 ──────────
        var distinctProducts = lines.Select(l => l.ProductCode).Distinct().ToList();
        var today = DateOnly.FromDateTime(DateTime.Today);
        var deadline = requestedDate > today ? requestedDate : today.AddDays(30);

        // 製品ごとのタスク
        var inventoryTasks = distinctProducts
            .Select(pc => (ProductCode: pc, Task: _sqlErp.GetProductInventoryAsync(pc, ct)))
            .ToList();
        var capacityTasks = distinctProducts
            .Select(pc => (ProductCode: pc, Task: _sqlErp.GetProductionCapacityAsync(pc, today, deadline, ct)))
            .ToList();

        // 与信は1回だけ
        var creditTask = _sqlErp.GetCustomerCreditAsync(customerCode, ct);

        // 全並列実行
        await Task.WhenAll(
            Task.WhenAll(inventoryTasks.Select(t => t.Task)),
            Task.WhenAll(capacityTasks.Select(t => t.Task)),
            creditTask);

        // ─── 2. 結果を辞書に整理 ──────────────────────────────────────────
        var inventoryMap = inventoryTasks.ToDictionary(
            t => t.ProductCode, t => t.Task.Result);
        var capacityMap = capacityTasks.ToDictionary(
            t => t.ProductCode, t => t.Task.Result);
        var credit = creditTask.Result;

        // ToolCallCard 追加
        toolCalls.Add(new ToolCallResult(
            "GetCustomerCredit", $"customerCode={customerCode}", credit.ToMarkdownTable()));
        foreach (var pc in distinctProducts)
        {
            toolCalls.Add(new ToolCallResult(
                "GetProductInventory", $"productCode={pc}", inventoryMap[pc].ToMarkdownTable()));
            toolCalls.Add(new ToolCallResult(
                "GetProductionCapacity",
                $"productCode={pc} from={today} to={deadline}",
                capacityMap[pc].ToMarkdownTable()));
        }

        // ─── 3. 与信・在庫の数値を抽出 ───────────────────────────────────
        var (creditLimit, usedAmount) = ExtractCreditValues(credit);
        var inventoryQtyMap = ExtractInventoryQty(inventoryMap);
        var capacityQtyMap = ExtractCapacityQty(capacityMap);

        // 受注合計金額（与信計算用）
        var pendingTotal = lines.Sum(l => l.Qty * l.UnitPrice);

        // ─── 4. 各行の判定（ICalcTool で決定的に計算） ────────────────────
        var result = lines.Select(l =>
        {
            var line = l; // mutable copy
            var qty = l.Qty;
            var onHand = inventoryQtyMap.GetValueOrDefault(l.ProductCode, 0);
            var capQty = capacityQtyMap.GetValueOrDefault(l.ProductCode, 0);
            var pendingAmt = qty * l.UnitPrice;

            var creditAvail = _calc.CreditAvailable(creditLimit, usedAmount, pendingTotal);

            // デモ用コスト仮定: 単価の75%を原価とする（実データがあれば SQL から取得）
            var estimatedCost = l.UnitPrice * 0.75m;
            var grossMargin = _calc.GrossMarginRatio(l.UnitPrice, estimatedCost);

            var verdict = Verdict.OK;
            var riskParts = new List<string>();

            // 在庫不足チェック
            if (qty > onHand + capQty)
            {
                verdict = Verdict.NG;
                riskParts.Add("在庫不足");
            }

            // 与信超過チェック
            if (creditAvail < 0)
            {
                verdict = Verdict.NG;
                riskParts.Add("与信超過");
            }
            else if (creditLimit > 0 && creditAvail < creditLimit * 0.2m)
            {
                if (verdict != Verdict.NG) verdict = Verdict.Warning;
                riskParts.Add("与信逼迫");
            }

            // 粗利不足チェック
            if (grossMargin < 0.15m)
            {
                if (verdict != Verdict.NG) verdict = Verdict.Warning;
                riskParts.Add("粗利不足");
            }

            line.Verdict = verdict;
            line.RiskLevel = string.Join(" / ", riskParts);
            line.RefNote = $"在庫{onHand}個 生産枠{capQty}個 与信残{creditAvail:N0}円 粗利率{grossMargin:P1}";

            return line;
        }).ToList();

        // ─── 5. NG/Warning 行のみ LLM に推奨文を生成させる ──────────────
        var riskLines = result.Where(l => l.Verdict != Verdict.OK).ToList();
        if (riskLines.Count > 0)
        {
            var recommendationTasks = riskLines.Select(async l =>
            {
                var prompt = $"""
                    製品「{l.ProductName}」({l.ProductCode})、数量{l.Qty}個、単価{l.UnitPrice:N0}円の見積について:
                    リスク分類: {l.RiskLevel}
                    参照情報: {l.RefNote}

                    営業担当者への推奨アクションを1〜2文で日本語で述べてください。数値計算はしないでください。
                    """;

                var messages = new List<ChatMessage>
                {
                    new(ChatRole.System, "あなたは営業支援エージェントです。リスクのある見積明細への推奨アクションを簡潔に述べてください。"),
                    new(ChatRole.User, prompt)
                };

                try
                {
                    var response = await _chatClient.GetResponseAsync(messages, cancellationToken: ct);
                    l.Recommendation = response.Text ?? "";
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "推奨文生成失敗: {ProductCode}", l.ProductCode);
                    l.Recommendation = $"リスクあり（{l.RiskLevel}）。営業マネージャーに確認してください。";
                }
            });
            await Task.WhenAll(recommendationTasks);
        }

        // ─── 6. 取込事実を observation として記録 ─────────────────────────
        var ngCount = result.Count(l => l.Verdict == Verdict.NG);
        var warnCount = result.Count(l => l.Verdict == Verdict.Warning);
        var observationText =
            $"Excel明細 {result.Count} 行取込・うち {ngCount + warnCount} 行リスク" +
            $"(NG:{ngCount} Warning:{warnCount})";

        try
        {
            await _contextStore.AppendObservationAsync(
                caseId, "Sales", "QuoteValidationService", observationText, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "observation 記録失敗: {CaseId}", caseId);
        }

        _logger.LogInformation(
            "QuoteValidationService.ValidateAsync: completed NG={NgCount} Warning={WarnCount}",
            ngCount, warnCount);

        return (result, toolCalls);
    }

    // ─── ヘルパ: 列名から列インデックスを取得 ────────────────────────────────
    private static int ColumnIndex(IReadOnlyList<string> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
            if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase))
                return i;
        return -1;
    }

    // ─── ヘルパ: SQL 結果から与信数値を抽出 ─────────────────────────────────
    private static (decimal CreditLimit, decimal UsedAmount) ExtractCreditValues(SqlQueryResult result)
    {
        // 列名: CreditLimit, UsedAmount（MockSqlErpTool / SqlErpTool の列定義に依存）
        var limitIdx = ColumnIndex(result.Columns, "CreditLimit");
        var usedIdx = ColumnIndex(result.Columns, "UsedAmount");

        if (result.Rows.Count == 0 || limitIdx < 0 || usedIdx < 0)
            return (0, 0);

        var row = result.Rows[0];
        var limit = ToDecimal(row.ElementAtOrDefault(limitIdx));
        var used = ToDecimal(row.ElementAtOrDefault(usedIdx));
        return (limit, used);
    }

    // ─── ヘルパ: SQL 結果から在庫数（OnHandQty）を抽出 ──────────────────────
    private static Dictionary<string, int> ExtractInventoryQty(
        Dictionary<string, SqlQueryResult> map)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (productCode, qr) in map)
        {
            var idx = ColumnIndex(qr.Columns, "OnHandQty");
            if (idx < 0) idx = ColumnIndex(qr.Columns, "AvailableQty");
            if (qr.Rows.Count > 0 && idx >= 0)
                result[productCode] = ToInt(qr.Rows[0].ElementAtOrDefault(idx));
            else
                result[productCode] = 0;
        }
        return result;
    }

    // ─── ヘルパ: SQL 結果から生産枠合計を抽出（サマリ行） ───────────────────
    private static Dictionary<string, int> ExtractCapacityQty(
        Dictionary<string, SqlQueryResult> map)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (productCode, qr) in map)
        {
            // サマリ行は末尾（plan.md: 末尾に合計サマリ行を付加）
            var idx = ColumnIndex(qr.Columns, "CapacityQty");
            if (idx < 0) idx = ColumnIndex(qr.Columns, "PlannedQty");
            if (qr.Rows.Count > 0 && idx >= 0)
            {
                // 全行の合計
                var total = qr.Rows.Sum(r => ToInt(r.ElementAtOrDefault(idx)));
                result[productCode] = total;
            }
            else
            {
                result[productCode] = 0;
            }
        }
        return result;
    }

    private static decimal ToDecimal(object? val)
    {
        if (val == null) return 0;
        return decimal.TryParse(val.ToString(), out var d) ? d : 0;
    }

    private static int ToInt(object? val)
    {
        if (val == null) return 0;
        return int.TryParse(val.ToString(), out var i) ? i : (int)ToDecimal(val);
    }
}
