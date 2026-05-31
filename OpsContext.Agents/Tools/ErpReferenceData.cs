namespace OpsContext.Agents.Tools;

/// <summary>
/// 基幹データのインメモリ正本（モックモード用・単一の真実）。
/// sql/seed.sql の値を流用しつつ、主役データ（A001/弁P-101）は
/// 既存デモシナリオと同値を維持する。
/// MockSqlErpTool と MockErpDatasetStore の両方がここを参照する。
/// </summary>
public static class ErpReferenceData
{
    // -----------------------------------------------------------------------
    // 顧客マスタ（10社）
    // CustomerCode / Name / Industry / CreditLimit / UsedAmount / RemainingCredit / CreditRating
    // 主役: A001 — 与信枠50M・使用38M・残12M・格付B（既存デモ同値）
    // -----------------------------------------------------------------------
    public sealed record CustomerRecord(
        string CustomerCode, string Name, string Industry,
        decimal CreditLimit, decimal UsedAmount, string CreditRating)
    {
        public decimal RemainingCredit => CreditLimit - UsedAmount;
    }

    public static readonly IReadOnlyList<CustomerRecord> Customers =
    [
        new("A001", "A商事株式会社",          "商社",      50_000_000m, 38_000_000m, "B"),
        new("A002", "B製造株式会社",          "製造業",    80_000_000m,          0m, "A"),
        new("A003", "Cフード株式会社",        "食品",      20_000_000m,  2_000_000m, "B"),
        new("A004", "Dサービス株式会社",      "サービス業",15_000_000m,  1_500_000m, "C"),
        new("A005", "Eテクノロジー株式会社",  "IT",        60_000_000m, 14_000_000m, "A"),
        new("A006", "Fロジスティクス株式会社","物流",      35_000_000m,  9_500_000m, "B"),
        new("A007", "G建設株式会社",          "建設",      25_000_000m,  7_300_000m, "B"),
        new("A008", "H医療株式会社",          "医療",      40_000_000m, 20_500_000m, "A"),
        new("A009", "I小売株式会社",          "小売",      10_000_000m,  1_100_000m, "C"),
        new("A010", "J農業株式会社",          "農業",      18_000_000m,  3_700_000m, "B"),
    ];

    // -----------------------------------------------------------------------
    // 在庫マスタ（20品番）
    // ProductCode / Name / Category / UnitPrice / OnHandQty / AllocatedQty / SafetyStock / AvailableQty
    // 主役: 弁P-101 — 手持80・引当0・安全在庫50・有効在庫80（既存デモ同値）
    // -----------------------------------------------------------------------
    public sealed record InventoryRecord(
        string ProductCode, string Name, string Category,
        decimal UnitPrice, int OnHandQty, int AllocatedQty, int SafetyStock)
    {
        public int AvailableQty => OnHandQty - AllocatedQty;
    }

    public static readonly IReadOnlyList<InventoryRecord> Inventory =
    [
        new("弁P-101",  "弁当用容器弁P-101",          "容器",      80_000m,   80,  0,  50),
        new("P-102",  "断熱容器P-102",             "容器",      95_000m,   45,  5,  20),
        new("P-103",  "仕切り付容器P-103",         "容器",      72_000m,  120, 10,  30),
        new("P-104",  "保冷容器P-104",             "容器",     120_000m,   30,  8,  15),
        new("P-201",  "包装フィルムS-201",         "包材",      15_000m,  500, 20, 100),
        new("P-202",  "包装フィルムM-202",         "包材",      18_000m,  400, 15,  80),
        new("P-203",  "包装フィルムL-203",         "包材",      22_000m,  350, 12,  60),
        new("P-204",  "シュリンクフィルム-204",    "包材",      12_000m,  600,  0, 100),
        new("P-301",  "緩衝材A-301",               "梱包材",     8_000m,  800, 30, 150),
        new("P-302",  "緩衝材B-302",               "梱包材",    10_000m,  600, 25, 100),
        new("P-303",  "段ボールS-303",             "梱包材",     5_000m, 1200, 50, 200),
        new("P-304",  "段ボールM-304",             "梱包材",     6_500m,  900, 40, 150),
        new("P-401",  "ラベルシールA-401",         "印刷物",     3_000m, 2000, 80, 300),
        new("P-402",  "パンフレットB-402",         "印刷物",    25_000m,  150, 10,  30),
        new("P-501",  "スチール棚S-501",           "設備",     150_000m,   12,  2,   5),
        new("P-502",  "スチール棚M-502",           "設備",     200_000m,    8,  1,   3),
        new("P-601",  "配送用パレット-601",        "物流",      35_000m,   90, 20,  30),
        new("P-602",  "ラック用トレー-602",        "物流",      28_000m,  200, 15,  50),
        new("P-701",  "洗浄剤クリーンA-701",       "消耗品",     4_500m, 5000,  0, 500),
        new("P-702",  "潤滑油ルブB-702",           "消耗品",     6_800m, 3000,  0, 300),
    ];

    // -----------------------------------------------------------------------
    // 生産能力サマリ（2週間・品番別）
    // ProductCode / CapacityPer14Days
    // 主役: 弁P-101 — 14日合計120個（既存デモ同値）
    // -----------------------------------------------------------------------
    public sealed record CapacityRecord(
        string ProductCode, string Name, int CapacityPer14Days, int CapacityPerDay)
    {
        public string Description => $"1日平均 {CapacityPerDay} 個 / 2週合計 {CapacityPer14Days} 個";
    }

    public static readonly IReadOnlyList<CapacityRecord> Capacity =
    [
        new("弁P-101", "弁当用容器弁P-101",           120,  9),
        new("P-102", "断熱容器P-102",             168, 12),
        new("P-103", "仕切り付容器P-103",         210, 15),
        new("P-104", "保冷容器P-104",              56,  4),
        new("P-201", "包装フィルムS-201",         700, 50),
        new("P-202", "包装フィルムM-202",         560, 40),
        new("P-203", "包装フィルムL-203",         420, 30),
        new("P-204", "シュリンクフィルム-204",    980, 70),
        new("P-301", "緩衝材A-301",              1400,100),
        new("P-302", "緩衝材B-302",              1120, 80),
        new("P-303", "段ボールS-303",            2100,150),
        new("P-304", "段ボールM-304",            1750,125),
        new("P-401", "ラベルシールA-401",        3500,250),
        new("P-402", "パンフレットB-402",          98,  7),
        new("P-501", "スチール棚S-501",            14,  1),
        new("P-502", "スチール棚M-502",            10,  1),
        new("P-601", "配送用パレット-601",        280, 20),
        new("P-602", "ラック用トレー-602",        420, 30),
        new("P-701", "洗浄剤クリーンA-701",      7000,500),
        new("P-702", "潤滑油ルブB-702",          4200,300),
    ];

    // -----------------------------------------------------------------------
    // 受注履歴（主役 A001 × 弁P-101 を含む代表的な履歴12件）
    // OrderNo / CustomerCode / CustomerName / ProductCode / Quantity / UnitPrice / OrderDate / Status
    // -----------------------------------------------------------------------
    public sealed record OrderRecord(
        string OrderNo, string CustomerCode, string CustomerName,
        string ProductCode, int Quantity, decimal UnitPrice,
        string OrderDate, string Status);

    public static readonly IReadOnlyList<OrderRecord> Orders =
    [
        new("ORD-0001", "A001", "A商事株式会社",          "弁P-101", 100, 80_000m, "2026-03-31", "Confirmed"),
        new("ORD-0002", "A001", "A商事株式会社",          "弁P-101", 120, 80_000m, "2026-04-15", "Confirmed"),
        new("ORD-0003", "A001", "A商事株式会社",          "弁P-101",  90, 80_000m, "2026-05-11", "Pending"),
        new("ORD-0004", "A001", "A商事株式会社",          "弁P-101",  80, 80_000m, "2026-05-24", "Pending"),
        new("ORD-0038", "A001", "A商事株式会社",          "弁P-101",  62, 80_000m, "2026-01-21", "Closed"),
        new("ORD-0039", "A001", "A商事株式会社",          "弁P-101",  87, 80_000m, "2026-02-10", "Closed"),
        new("ORD-0005", "A002", "B製造株式会社",          "P-102",  40, 95_000m, "2026-04-11", "Shipped"),
        new("ORD-0006", "A002", "B製造株式会社",          "P-501",  40,150_000m, "2026-05-01", "Confirmed"),
        new("ORD-0010", "A003", "Cフード株式会社",        "P-103",  20, 72_000m, "2026-04-21", "Shipped"),
        new("ORD-0016", "A005", "Eテクノロジー株式会社",  "P-501",  80,150_000m, "2026-02-20", "Closed"),
        new("ORD-0027", "A008", "H医療株式会社",          "P-104",  75,120_000m, "2026-04-11", "Shipped"),
        new("ORD-0036", "A010", "J農業株式会社",          "P-201", 100, 15_000m, "2026-05-18", "Confirmed"),
    ];

    // -----------------------------------------------------------------------
    // Lookup ヘルパ（MockSqlErpTool から呼ぶ）
    // -----------------------------------------------------------------------

    /// <summary>顧客コードから顧客レコードを返す。見つからなければダミーを返す。</summary>
    public static CustomerRecord GetCustomer(string code)
    {
        var c = Customers.FirstOrDefault(x =>
            string.Equals(x.CustomerCode, code, StringComparison.OrdinalIgnoreCase));
        return c ?? new CustomerRecord(code, "サンプル顧客", "一般", 10_000_000m, 0m, "A");
    }

    /// <summary>品番から在庫レコードを返す。見つからなければダミーを返す。</summary>
    public static InventoryRecord GetInventory(string productCode)
    {
        var normalized = NormalizeProductCode(productCode);
        var r = Inventory.FirstOrDefault(x =>
            string.Equals(x.ProductCode, normalized, StringComparison.OrdinalIgnoreCase));
        return r ?? new InventoryRecord(productCode, "（汎用品）", "一般", 10_000m, 500, 10, 50);
    }

    /// <summary>品番から生産能力レコードを返す。見つからなければダミーを返す。</summary>
    public static CapacityRecord GetCapacity(string productCode)
    {
        var normalized = NormalizeProductCode(productCode);
        var r = Capacity.FirstOrDefault(x =>
            string.Equals(x.ProductCode, normalized, StringComparison.OrdinalIgnoreCase));
        return r ?? new CapacityRecord(productCode, "（汎用品）", 999, 71);
    }

    /// <summary>
    /// 品番の表記ゆれを正規化する。
    /// 「弁P-101」→「弁P-101」などのエイリアスを解決する。
    /// </summary>
    public static string NormalizeProductCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        // 先頭の漢字・ひらがな・カタカナプレフィックスを除去し "P-NNN" 形式を抽出
        var m = System.Text.RegularExpressions.Regex.Match(raw, @"[A-Za-z]-?\d{3,}");
        if (m.Success) return m.Value.ToUpperInvariant();
        return raw.Trim();
    }
}
