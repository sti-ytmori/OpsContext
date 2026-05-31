namespace OpsContext.Agents.Tools;

// モックモード用 SqlErpTool。
// ErpReferenceData（単一の真実）を参照してインメモリデータを返す。
// 画面で見えるデータセット値と突合結果が必ず一致する。
// 弁P-101 などの表記ゆれは ErpReferenceData.NormalizeProductCode で解決する。
public sealed class MockSqlErpTool : ISqlErpTool
{
    public Task<SqlQueryResult> GetCustomerCreditAsync(string customerCode, CancellationToken ct)
    {
        var c = ErpReferenceData.GetCustomer(customerCode);
        var cols = new[] { "CustomerCode", "Name", "CreditLimit", "UsedAmount", "RemainingCredit", "CreditRating" };
        IReadOnlyList<object?> row = new object?[]
        {
            c.CustomerCode, c.Name,
            c.CreditLimit, c.UsedAmount, c.RemainingCredit,
            c.CreditRating
        };
        return Task.FromResult(new SqlQueryResult(cols, [row], 1));
    }

    public Task<SqlQueryResult> GetProductInventoryAsync(string productCode, CancellationToken ct)
    {
        var r = ErpReferenceData.GetInventory(productCode);
        var cols = new[] { "ProductCode", "OnHandQty", "AllocatedQty", "SafetyStock", "AvailableQty" };
        IReadOnlyList<object?> row = new object?[]
        {
            r.ProductCode, r.OnHandQty, r.AllocatedQty, r.SafetyStock, r.AvailableQty
        };
        return Task.FromResult(new SqlQueryResult(cols, [row], 1));
    }

    public Task<SqlQueryResult> GetProductionCapacityAsync(
        string productCode, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var cap = ErpReferenceData.GetCapacity(productCode);
        var cols = new[] { "CapacityDate", "AvailableUnits", "ReservedUnits" };
        var rows = new List<IReadOnlyList<object?>>();

        // 日別を from〜to で展開（AvailableUnits を均等割り）
        var current = from;
        int dayIndex = 0;
        while (current <= to)
        {
            // 主役 弁P-101 は 9/8 交互パターン、他は均等
            int avail = (cap.ProductCode == "弁P-101")
                ? ((dayIndex % 2 == 0) ? 9 : 8)
                : cap.CapacityPerDay;
            rows.Add(new object?[] { current.ToString("yyyy-MM-dd"), avail, 0 });
            current = current.AddDays(1);
            dayIndex++;
        }

        // 末尾に合計サマリ行（QuoteValidationService は CapacityQty / PlannedQty 列を探すが
        // 本モックは AvailableUnits 列を合計して返す）
        int totalAvail = rows.Sum(r => (int)(r[1] ?? 0));
        rows.Add(new object?[] { "合計", totalAvail, 0 });

        return Task.FromResult(new SqlQueryResult(cols, rows, rows.Count));
    }

    public Task<SqlQueryResult> SearchSimilarOrdersAsync(
        string customerCode, string productCode, int topN, CancellationToken ct)
    {
        var normalized = ErpReferenceData.NormalizeProductCode(productCode);
        var cols = new[] { "OrderNo", "CustomerCode", "ProductCode", "Quantity", "OrderDate", "Status" };

        var rows = ErpReferenceData.Orders
            .Where(o =>
                string.Equals(o.CustomerCode, customerCode, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    ErpReferenceData.NormalizeProductCode(o.ProductCode),
                    normalized,
                    StringComparison.OrdinalIgnoreCase))
            .Take(topN)
            .Select(o => (IReadOnlyList<object?>)new object?[]
            {
                o.OrderNo, o.CustomerCode, o.ProductCode, o.Quantity, o.OrderDate, o.Status
            })
            .ToList();

        return Task.FromResult(new SqlQueryResult(cols, rows, rows.Count));
    }

    public Task<SqlQueryResult> RunReadOnlyQueryAsync(string sql, CancellationToken ct)
    {
        SqlReadOnlyGuard.Validate(sql);

        // WHERE 句の最初の TableId 指定からテーブルを特定してモックデータを返す
        var whereStart = sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase);
        var searchArea = whereStart >= 0 ? sql[whereStart..] : sql;
        var tableIdMatch = System.Text.RegularExpressions.Regex.Match(
            searchArea, @"TableId\s*=\s*'([^']+)'", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (tableIdMatch.Success)
        {
            return tableIdMatch.Groups[1].Value switch
            {
                "erp_inventory"           => Task.FromResult(BuildInventoryResult()),
                "erp_customers"           => Task.FromResult(BuildCustomerResult()),
                "erp_orders"              => Task.FromResult(BuildOrderResult()),
                "erp_production_capacity" => Task.FromResult(BuildCapacityResult()),
                _ => Task.FromResult(new SqlQueryResult(["Result"], [["(モック: テーブルデータなし)"]], 1))
            };
        }

        return Task.FromResult(new SqlQueryResult(["Result"], [["(モック: クエリ実行をシミュレート)"]], 1));
    }

    private static SqlQueryResult BuildInventoryResult()
    {
        var cols = new[] { "ProductCode", "Name", "Category", "UnitPrice", "OnHandQty", "AllocatedQty", "SafetyStock", "AvailableQty" };
        var rows = ErpReferenceData.Inventory
            .Select(i => (IReadOnlyList<object?>)new object?[]
            {
                i.ProductCode, i.Name, i.Category,
                i.UnitPrice, i.OnHandQty, i.AllocatedQty, i.SafetyStock, i.AvailableQty
            })
            .ToList();
        return new SqlQueryResult(cols, rows, rows.Count);
    }

    private static SqlQueryResult BuildCustomerResult()
    {
        var cols = new[] { "CustomerCode", "Name", "Industry", "CreditLimit", "UsedAmount", "RemainingCredit", "CreditRating" };
        var rows = ErpReferenceData.Customers
            .Select(c => (IReadOnlyList<object?>)new object?[]
            {
                c.CustomerCode, c.Name, c.Industry,
                c.CreditLimit, c.UsedAmount, c.RemainingCredit, c.CreditRating
            })
            .ToList();
        return new SqlQueryResult(cols, rows, rows.Count);
    }

    private static SqlQueryResult BuildOrderResult()
    {
        var cols = new[] { "OrderNo", "CustomerCode", "CustomerName", "ProductCode", "Quantity", "UnitPrice", "OrderDate", "Status" };
        var rows = ErpReferenceData.Orders
            .Select(o => (IReadOnlyList<object?>)new object?[]
            {
                o.OrderNo, o.CustomerCode, o.CustomerName,
                o.ProductCode, o.Quantity, o.UnitPrice, o.OrderDate, o.Status
            })
            .ToList();
        return new SqlQueryResult(cols, rows, rows.Count);
    }

    private static SqlQueryResult BuildCapacityResult()
    {
        var cols = new[] { "ProductCode", "Name", "CapacityPer14Days", "CapacityPerDay" };
        var rows = ErpReferenceData.Capacity
            .Select(c => (IReadOnlyList<object?>)new object?[]
            {
                c.ProductCode, c.Name, c.CapacityPer14Days, c.CapacityPerDay
            })
            .ToList();
        return new SqlQueryResult(cols, rows, rows.Count);
    }
}
