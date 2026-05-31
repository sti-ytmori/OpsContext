using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

/// <summary>
/// インメモリ実装の IErpDatasetStore。
/// ErpReferenceData から ErpDatasetView を構築して返す。
/// </summary>
public sealed class MockErpDatasetStore : IErpDatasetStore
{
    private static readonly IReadOnlyList<ErpDatasetMeta> _metas =
    [
        new("customers", "顧客マスタ",  "与信枠・使用額・与信格付け",           "account_balance"),
        new("inventory", "製品在庫",    "在庫数・引当数・安全在庫・有効在庫",   "inventory_2"),
        new("capacity",  "生産能力",    "品番別の2週間生産枠（日次合計）",       "precision_manufacturing"),
        new("orders",    "受注履歴",    "直近の受注明細（顧客・品番・数量）",   "receipt_long"),
    ];

    private DateTimeOffset _lastSyncedAt = DateTimeOffset.UtcNow.AddHours(-6);

    public IReadOnlyList<ErpDatasetMeta> ListDatasets() => _metas;

    public ErpDatasetView? GetDataset(string key) => key switch
    {
        "customers" => BuildCustomers(),
        "inventory" => BuildInventory(),
        "capacity"  => BuildCapacity(),
        "orders"    => BuildOrders(),
        _           => null
    };

    public DateTimeOffset LastSyncedAt => _lastSyncedAt;

    public Task<DateTimeOffset> TriggerSyncAsync(CancellationToken ct = default)
    {
        _lastSyncedAt = DateTimeOffset.UtcNow;
        return Task.FromResult(_lastSyncedAt);
    }

    // -----------------------------------------------------------------------
    // ビルダー
    // -----------------------------------------------------------------------

    private static ErpDatasetView BuildCustomers()
    {
        string[] cols = ["顧客コード", "顧客名", "業種", "与信枠（円）", "使用額（円）", "残枠（円）", "格付け"];
        var rows = ErpReferenceData.Customers.Select(c => new string?[]
        {
            c.CustomerCode, c.Name, c.Industry,
            c.CreditLimit.ToString("N0"),
            c.UsedAmount.ToString("N0"),
            c.RemainingCredit.ToString("N0"),
            c.CreditRating
        }).ToList<IReadOnlyList<string?>>();

        return new("customers", "顧客マスタ", "与信枠・使用額・与信格付け", cols, rows);
    }

    private static ErpDatasetView BuildInventory()
    {
        string[] cols = ["品番", "品名", "カテゴリ", "単価（円）", "手持数", "引当数", "安全在庫", "有効在庫"];
        var rows = ErpReferenceData.Inventory.Select(r => new string?[]
        {
            r.ProductCode, r.Name, r.Category,
            r.UnitPrice.ToString("N0"),
            r.OnHandQty.ToString(),
            r.AllocatedQty.ToString(),
            r.SafetyStock.ToString(),
            r.AvailableQty.ToString()
        }).ToList<IReadOnlyList<string?>>();

        return new("inventory", "製品在庫", "在庫数・引当数・安全在庫・有効在庫", cols, rows);
    }

    private static ErpDatasetView BuildCapacity()
    {
        string[] cols = ["品番", "品名", "1日平均（個）", "2週間合計（個）"];
        var rows = ErpReferenceData.Capacity.Select(r => new string?[]
        {
            r.ProductCode, r.Name,
            r.CapacityPerDay.ToString(),
            r.CapacityPer14Days.ToString()
        }).ToList<IReadOnlyList<string?>>();

        return new("capacity", "生産能力", "品番別の2週間生産枠（日次合計）", cols, rows);
    }

    private static ErpDatasetView BuildOrders()
    {
        string[] cols = ["受注No", "顧客コード", "顧客名", "品番", "数量", "単価（円）", "受注日", "ステータス"];
        var rows = ErpReferenceData.Orders.Select(o => new string?[]
        {
            o.OrderNo, o.CustomerCode, o.CustomerName, o.ProductCode,
            o.Quantity.ToString(),
            o.UnitPrice.ToString("N0"),
            o.OrderDate, o.Status
        }).ToList<IReadOnlyList<string?>>();

        return new("orders", "受注履歴", "直近の受注明細（顧客・品番・数量）", cols, rows);
    }
}
