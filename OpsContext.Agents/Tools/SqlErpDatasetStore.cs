using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Models;
using OpsContext.Agents.Options;

namespace OpsContext.Agents.Tools;

/// <summary>
/// 本番モード用 IErpDatasetStore。
/// ERPスナップショットテーブル（ErpCustomers / ErpInventory / ErpProducts / ErpProductionCapacity / ErpOrders / ErpOrderLines）
/// を SELECT して ErpDatasetView を構築する。
/// テーブルが存在しない場合は空データ＋警告ログでフォールバックする。
/// LastSyncedAt は ErpSnapshotMeta テーブルから取得する（テーブル未作成時は起動時刻を使用）。
/// </summary>
public sealed class SqlErpDatasetStore : IErpDatasetStore
{
    private static readonly IReadOnlyList<ErpDatasetMeta> _metas =
    [
        new("customers", "顧客マスタ",  "与信枠・使用額・与信格付け",           "account_balance"),
        new("inventory", "製品在庫",    "在庫数・引当数・安全在庫・有効在庫",   "inventory_2"),
        new("capacity",  "生産能力",    "品番別の2週間生産枠（日次合計）",       "precision_manufacturing"),
        new("orders",    "受注履歴",    "直近の受注明細（顧客・品番・数量）",   "receipt_long"),
    ];

    private readonly string _connectionString;
    private readonly ILogger<SqlErpDatasetStore> _logger;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public SqlErpDatasetStore(
        IOptions<OpsContextOptions> opts,
        ILogger<SqlErpDatasetStore> logger)
    {
        _connectionString = opts.Value.SqlConnectionString;
        _logger = logger;
    }

    public IReadOnlyList<ErpDatasetMeta> ListDatasets() => _metas;

    public ErpDatasetView? GetDataset(string key) => key switch
    {
        "customers" => FetchDataset(key, "顧客マスタ", "与信枠・使用額・与信格付け", BuildCustomersQuery()),
        "inventory" => FetchDataset(key, "製品在庫", "在庫数・引当数・安全在庫・有効在庫", BuildInventoryQuery()),
        "capacity"  => FetchDataset(key, "生産能力", "品番別の2週間生産枠", BuildCapacityQuery()),
        "orders"    => FetchDataset(key, "受注履歴", "直近の受注明細（顧客・品番・数量）", BuildOrdersQuery()),
        _           => null
    };

    public DateTimeOffset LastSyncedAt
    {
        get
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using var cmd = new SqlCommand(
                    "SELECT TOP 1 LastSyncedAt FROM ErpSnapshotMeta ORDER BY LastSyncedAt DESC", conn);
                var val = cmd.ExecuteScalar();
                if (val is DateTime dt) return new DateTimeOffset(dt, TimeSpan.Zero);
            }
            catch
            {
                // テーブル未作成などは握りつぶし、起動時刻を返す
            }
            return _startedAt;
        }
    }

    public async Task<DateTimeOffset> TriggerSyncAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            await using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("""
                IF OBJECT_ID('ErpSnapshotMeta','U') IS NOT NULL
                BEGIN
                    DELETE FROM ErpSnapshotMeta;
                    INSERT INTO ErpSnapshotMeta (LastSyncedAt) VALUES (@Now);
                END
                """, conn);
            cmd.Parameters.AddWithValue("@Now", now.UtcDateTime);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ErpSnapshotMeta 更新失敗（テーブル未作成の可能性あり）");
        }
        return now;
    }

    // -----------------------------------------------------------------------
    // 共通フェッチ
    // -----------------------------------------------------------------------

    private ErpDatasetView FetchDataset(string key, string displayName, string description, (string Sql, string[] Columns) query)
    {
        var rows = new List<IReadOnlyList<string?>>();
        try
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new SqlCommand(query.Sql, conn) { CommandTimeout = 15 };
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var row = new string?[reader.FieldCount];
                for (int i = 0; i < reader.FieldCount; i++)
                    row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i)?.ToString();
                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ErpDatasetStore: {Key} の取得に失敗しました", key);
        }
        return new(key, displayName, description, query.Columns, rows);
    }

    // -----------------------------------------------------------------------
    // クエリ定義
    // -----------------------------------------------------------------------

    private static (string Sql, string[] Columns) BuildCustomersQuery() => (
        """
        SELECT
            JSON_VALUE(c.CellsJson, '$.CustomerCode')                                        AS CustomerCode,
            JSON_VALUE(c.CellsJson, '$.Name')                                                AS Name,
            JSON_VALUE(c.CellsJson, '$.Industry')                                            AS Industry,
            CAST(JSON_VALUE(c.CellsJson, '$.CreditLimit') AS DECIMAL(18,0))                  AS CreditLimit,
            ISNULL(SUM(CAST(JSON_VALUE(o.CellsJson, '$.TotalAmount') AS DECIMAL(18,0))), 0)  AS UsedAmount,
            CAST(JSON_VALUE(c.CellsJson, '$.CreditLimit') AS DECIMAL(18,0))
                - ISNULL(SUM(CAST(JSON_VALUE(o.CellsJson, '$.TotalAmount') AS DECIMAL(18,0))), 0)
                                                                                             AS RemainingCredit,
            JSON_VALUE(c.CellsJson, '$.CreditRating')                                        AS CreditRating
        FROM GridRows c
        LEFT JOIN GridRows o
            ON  o.TableId = 'erp_orders'
            AND JSON_VALUE(o.CellsJson, '$.CustomerCode') = JSON_VALUE(c.CellsJson, '$.CustomerCode')
            AND JSON_VALUE(o.CellsJson, '$.Status') IN ('Pending','Confirmed')
        WHERE c.TableId = 'erp_customers'
        GROUP BY c.CellsJson
        ORDER BY JSON_VALUE(c.CellsJson, '$.CustomerCode')
        """,
        ["顧客コード", "顧客名", "業種", "与信枠（円）", "使用額（円）", "残枠（円）", "格付け"]
    );

    private static (string Sql, string[] Columns) BuildInventoryQuery() => (
        """
        SELECT
            JSON_VALUE(i.CellsJson, '$.ProductCode')                              AS ProductCode,
            JSON_VALUE(p.CellsJson, '$.Name')                                     AS Name,
            JSON_VALUE(p.CellsJson, '$.Category')                                 AS Category,
            CAST(JSON_VALUE(p.CellsJson, '$.UnitPrice')    AS DECIMAL(18,0))      AS UnitPrice,
            CAST(JSON_VALUE(i.CellsJson, '$.OnHandQty')    AS INT)                AS OnHandQty,
            CAST(JSON_VALUE(i.CellsJson, '$.AllocatedQty') AS INT)                AS AllocatedQty,
            CAST(JSON_VALUE(i.CellsJson, '$.SafetyStock')  AS INT)                AS SafetyStock,
            CAST(JSON_VALUE(i.CellsJson, '$.OnHandQty')    AS INT)
                - CAST(JSON_VALUE(i.CellsJson, '$.AllocatedQty') AS INT)          AS AvailableQty
        FROM GridRows i
        INNER JOIN GridRows p
            ON  p.TableId = 'erp_products'
            AND JSON_VALUE(p.CellsJson, '$.ProductCode') = JSON_VALUE(i.CellsJson, '$.ProductCode')
        WHERE i.TableId = 'erp_inventory'
        ORDER BY JSON_VALUE(i.CellsJson, '$.ProductCode')
        """,
        ["品番", "品名", "カテゴリ", "単価（円）", "手持数", "引当数", "安全在庫", "有効在庫"]
    );

    private static (string Sql, string[] Columns) BuildCapacityQuery() => (
        """
        SELECT
            JSON_VALUE(r.CellsJson, '$.ProductCode')                              AS ProductCode,
            AVG(CAST(JSON_VALUE(r.CellsJson, '$.AvailableUnits') AS INT))         AS AvgPerDay,
            SUM(CAST(JSON_VALUE(r.CellsJson, '$.AvailableUnits') AS INT))         AS TotalCapacity
        FROM GridRows r
        WHERE r.TableId = 'erp_production_capacity'
          AND CAST(JSON_VALUE(r.CellsJson, '$.CapacityDate') AS DATE)
              BETWEEN CAST(GETDATE() AS DATE)
                  AND CAST(DATEADD(day, 14, GETDATE()) AS DATE)
        GROUP BY JSON_VALUE(r.CellsJson, '$.ProductCode')
        ORDER BY JSON_VALUE(r.CellsJson, '$.ProductCode')
        """,
        ["品番", "1日平均（個）", "2週間合計（個）"]
    );

    private static (string Sql, string[] Columns) BuildOrdersQuery() => (
        """
        SELECT TOP 50
            JSON_VALUE(o.CellsJson,  '$.OrderNo')                                 AS OrderNo,
            JSON_VALUE(o.CellsJson,  '$.CustomerCode')                            AS CustomerCode,
            JSON_VALUE(c.CellsJson,  '$.Name')                                    AS CustomerName,
            JSON_VALUE(ol.CellsJson, '$.ProductCode')                             AS ProductCode,
            CAST(JSON_VALUE(ol.CellsJson, '$.Quantity')  AS INT)                  AS Quantity,
            CAST(JSON_VALUE(ol.CellsJson, '$.UnitPrice') AS DECIMAL(18,0))        AS UnitPrice,
            JSON_VALUE(o.CellsJson,  '$.OrderDate')                               AS OrderDate,
            JSON_VALUE(o.CellsJson,  '$.Status')                                  AS Status
        FROM GridRows ol
        INNER JOIN GridRows o
            ON  o.TableId = 'erp_orders'
            AND JSON_VALUE(o.CellsJson, '$.OrderNo') = JSON_VALUE(ol.CellsJson, '$.OrderNo')
        INNER JOIN GridRows c
            ON  c.TableId = 'erp_customers'
            AND JSON_VALUE(c.CellsJson, '$.CustomerCode') = JSON_VALUE(o.CellsJson, '$.CustomerCode')
        WHERE ol.TableId = 'erp_orderlines'
        ORDER BY JSON_VALUE(o.CellsJson, '$.OrderDate') DESC
        """,
        ["受注No", "顧客コード", "顧客名", "品番", "数量", "単価（円）", "受注日", "ステータス"]
    );
}
