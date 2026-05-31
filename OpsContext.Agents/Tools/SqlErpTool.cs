using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Options;

namespace OpsContext.Agents.Tools;

// design 02「ISqlErpTool インターフェース」に準拠。
// ERP基幹データは GridTables/GridRows の論理テーブルに格納されており、
// JSON_VALUE で各フィールドを取り出してクエリする。
public sealed class SqlErpTool : ISqlErpTool
{
    private readonly string _connectionString;
    private readonly ILogger<SqlErpTool> _logger;

    public SqlErpTool(IOptions<OpsContextOptions> opts, ILogger<SqlErpTool> logger)
    {
        _connectionString = opts.Value.SqlConnectionString;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // GetCustomerCreditAsync
    // erp_customers + erp_orders(Pending/Confirmed) を JSON_VALUE で JOIN し
    // 与信枠・使用額・残額を 1 行で返す。
    // -----------------------------------------------------------------------
    public async Task<SqlQueryResult> GetCustomerCreditAsync(
        string customerCode, CancellationToken ct)
    {
        const string sql = """
            SELECT
                JSON_VALUE(c.CellsJson, '$.CustomerCode')                                   AS CustomerCode,
                JSON_VALUE(c.CellsJson, '$.Name')                                           AS Name,
                CAST(JSON_VALUE(c.CellsJson, '$.CreditLimit')     AS DECIMAL(18,0))         AS CreditLimit,
                JSON_VALUE(c.CellsJson, '$.CreditRating')                                   AS CreditRating,
                CAST(JSON_VALUE(c.CellsJson, '$.PaymentTermDays') AS INT)                   AS PaymentTermDays,
                ISNULL(SUM(CAST(JSON_VALUE(o.CellsJson, '$.TotalAmount') AS DECIMAL(18,0))), 0)
                                                                                             AS UsedCredit,
                CAST(JSON_VALUE(c.CellsJson, '$.CreditLimit') AS DECIMAL(18,0))
                    - ISNULL(SUM(CAST(JSON_VALUE(o.CellsJson, '$.TotalAmount') AS DECIMAL(18,0))), 0)
                                                                                             AS RemainingCredit
            FROM GridRows c
            LEFT JOIN GridRows o
                ON  o.TableId = 'erp_orders'
                AND JSON_VALUE(o.CellsJson, '$.CustomerCode') = JSON_VALUE(c.CellsJson, '$.CustomerCode')
                AND JSON_VALUE(o.CellsJson, '$.Status') IN ('Pending', 'Confirmed')
            WHERE c.TableId = 'erp_customers'
              AND JSON_VALUE(c.CellsJson, '$.CustomerCode') = @CustomerCode
            GROUP BY c.CellsJson
            """;

        var parameters = new[] { new SqlParameter("@CustomerCode", customerCode) };
        _logger.LogDebug("GetCustomerCreditAsync: customerCode={CustomerCode}", customerCode);
        return await ExecuteQueryAsync(sql, parameters, ct);
    }

    // -----------------------------------------------------------------------
    // GetProductInventoryAsync
    // erp_inventory + erp_products を JSON_VALUE で JOIN し
    // AvailableQty = OnHandQty - AllocatedQty を計算列として返す。
    // -----------------------------------------------------------------------
    public async Task<SqlQueryResult> GetProductInventoryAsync(
        string productCode, CancellationToken ct)
    {
        productCode = ErpReferenceData.NormalizeProductCode(productCode);
        const string sql = """
            SELECT
                JSON_VALUE(i.CellsJson, '$.ProductCode')                                     AS ProductCode,
                JSON_VALUE(p.CellsJson, '$.Name')                                            AS Name,
                CAST(JSON_VALUE(p.CellsJson, '$.UnitPrice')    AS DECIMAL(18,0))             AS UnitPrice,
                CAST(JSON_VALUE(p.CellsJson, '$.UnitCost')     AS DECIMAL(18,0))             AS UnitCost,
                CAST(JSON_VALUE(p.CellsJson, '$.LeadTimeDays') AS INT)                       AS LeadTimeDays,
                CAST(JSON_VALUE(i.CellsJson, '$.OnHandQty')    AS INT)                       AS OnHandQty,
                CAST(JSON_VALUE(i.CellsJson, '$.AllocatedQty') AS INT)                       AS AllocatedQty,
                CAST(JSON_VALUE(i.CellsJson, '$.SafetyStock')  AS INT)                       AS SafetyStock,
                CAST(JSON_VALUE(i.CellsJson, '$.OnHandQty')    AS INT)
                    - CAST(JSON_VALUE(i.CellsJson, '$.AllocatedQty') AS INT)                 AS AvailableQty,
                JSON_VALUE(i.CellsJson, '$.LastUpdated')                                     AS LastUpdated
            FROM GridRows i
            INNER JOIN GridRows p
                ON  p.TableId = 'erp_products'
                AND JSON_VALUE(p.CellsJson, '$.ProductCode') = JSON_VALUE(i.CellsJson, '$.ProductCode')
            WHERE i.TableId = 'erp_inventory'
              AND JSON_VALUE(i.CellsJson, '$.ProductCode') = @ProductCode
            """;

        var parameters = new[] { new SqlParameter("@ProductCode", productCode) };
        _logger.LogDebug("GetProductInventoryAsync: productCode={ProductCode}", productCode);
        return await ExecuteQueryAsync(sql, parameters, ct);
    }

    // -----------------------------------------------------------------------
    // GetProductionCapacityAsync
    // erp_production_capacity を日付範囲でフィルタし、末尾に合計サマリ行を付加。
    // -----------------------------------------------------------------------
    public async Task<SqlQueryResult> GetProductionCapacityAsync(
        string productCode, DateOnly from, DateOnly to, CancellationToken ct)
    {
        productCode = ErpReferenceData.NormalizeProductCode(productCode);
        const string sql = """
            SELECT
                JSON_VALUE(r.CellsJson, '$.ProductCode')                                     AS ProductCode,
                JSON_VALUE(r.CellsJson, '$.CapacityDate')                                    AS CapacityDate,
                CAST(JSON_VALUE(r.CellsJson, '$.AvailableUnits') AS INT)                     AS AvailableUnits,
                CAST(JSON_VALUE(r.CellsJson, '$.ReservedUnits')  AS INT)                     AS ReservedUnits,
                CAST(JSON_VALUE(r.CellsJson, '$.AvailableUnits') AS INT)
                    - CAST(JSON_VALUE(r.CellsJson, '$.ReservedUnits') AS INT)                AS FreeUnits
            FROM GridRows r
            WHERE r.TableId = 'erp_production_capacity'
              AND JSON_VALUE(r.CellsJson, '$.ProductCode') = @ProductCode
              AND CAST(JSON_VALUE(r.CellsJson, '$.CapacityDate') AS DATE) BETWEEN @From AND @To

            UNION ALL

            SELECT
                @ProductCode                                                                  AS ProductCode,
                '__TOTAL__'                                                                   AS CapacityDate,
                SUM(CAST(JSON_VALUE(r.CellsJson, '$.AvailableUnits') AS INT))                AS AvailableUnits,
                SUM(CAST(JSON_VALUE(r.CellsJson, '$.ReservedUnits')  AS INT))                AS ReservedUnits,
                SUM(CAST(JSON_VALUE(r.CellsJson, '$.AvailableUnits') AS INT)
                    - CAST(JSON_VALUE(r.CellsJson, '$.ReservedUnits') AS INT))               AS FreeUnits
            FROM GridRows r
            WHERE r.TableId = 'erp_production_capacity'
              AND JSON_VALUE(r.CellsJson, '$.ProductCode') = @ProductCode
              AND CAST(JSON_VALUE(r.CellsJson, '$.CapacityDate') AS DATE) BETWEEN @From AND @To

            ORDER BY CapacityDate
            """;

        var parameters = new[]
        {
            new SqlParameter("@ProductCode", productCode),
            new SqlParameter("@From",        from.ToDateTime(TimeOnly.MinValue)),
            new SqlParameter("@To",          to.ToDateTime(TimeOnly.MinValue))
        };

        _logger.LogDebug(
            "GetProductionCapacityAsync: productCode={ProductCode} from={From} to={To}",
            productCode, from, to);
        return await ExecuteQueryAsync(sql, parameters, ct);
    }

    // -----------------------------------------------------------------------
    // SearchSimilarOrdersAsync
    // erp_orderlines + erp_orders + erp_customers を JSON_VALUE で JOIN し
    // 同一 ProductCode の過去受注を OrderDate 降順で topN 件返す。
    // -----------------------------------------------------------------------
    public async Task<SqlQueryResult> SearchSimilarOrdersAsync(
        string customerCode, string productCode, int topN, CancellationToken ct)
    {
        if (topN <= 0 || topN > 1000)
            throw new ArgumentOutOfRangeException(nameof(topN), "topN は 1〜1000 の範囲で指定してください。");

        productCode = ErpReferenceData.NormalizeProductCode(productCode);
        string sql = $"""
            SELECT TOP ({topN})
                JSON_VALUE(o.CellsJson, '$.OrderNo')                                         AS OrderNo,
                JSON_VALUE(o.CellsJson, '$.OrderDate')                                       AS OrderDate,
                JSON_VALUE(o.CellsJson, '$.Status')                                          AS Status,
                CAST(JSON_VALUE(o.CellsJson, '$.TotalAmount') AS DECIMAL(18,0))              AS TotalAmount,
                JSON_VALUE(c.CellsJson, '$.CustomerCode')                                    AS CustomerCode,
                JSON_VALUE(c.CellsJson, '$.Name')                                            AS CustomerName,
                JSON_VALUE(ol.CellsJson, '$.ProductCode')                                    AS ProductCode,
                CAST(JSON_VALUE(ol.CellsJson, '$.Quantity')   AS INT)                        AS Quantity,
                CAST(JSON_VALUE(ol.CellsJson, '$.UnitPrice')  AS DECIMAL(18,0))              AS UnitPrice,
                CAST(JSON_VALUE(ol.CellsJson, '$.LineAmount') AS DECIMAL(18,0))              AS LineAmount
            FROM GridRows ol
            INNER JOIN GridRows o
                ON  o.TableId = 'erp_orders'
                AND JSON_VALUE(o.CellsJson, '$.OrderNo') = JSON_VALUE(ol.CellsJson, '$.OrderNo')
            INNER JOIN GridRows c
                ON  c.TableId = 'erp_customers'
                AND JSON_VALUE(c.CellsJson, '$.CustomerCode') = JSON_VALUE(o.CellsJson, '$.CustomerCode')
            WHERE ol.TableId = 'erp_orderlines'
              AND JSON_VALUE(ol.CellsJson, '$.ProductCode')   = @ProductCode
              AND JSON_VALUE(o.CellsJson,  '$.CustomerCode')  = @CustomerCode
            ORDER BY JSON_VALUE(o.CellsJson, '$.OrderDate') DESC
            """;

        var parameters = new[]
        {
            new SqlParameter("@CustomerCode", customerCode),
            new SqlParameter("@ProductCode",  productCode)
        };

        _logger.LogDebug(
            "SearchSimilarOrdersAsync: customerCode={CustomerCode} productCode={ProductCode} topN={TopN}",
            customerCode, productCode, topN);
        return await ExecuteQueryAsync(sql, parameters, ct);
    }

    // -----------------------------------------------------------------------
    // RunReadOnlyQueryAsync
    // SqlReadOnlyGuard.Validate を通過した場合のみ実行する。
    // -----------------------------------------------------------------------
    public async Task<SqlQueryResult> RunReadOnlyQueryAsync(string sql, CancellationToken ct)
    {
        SqlReadOnlyGuard.Validate(sql);
        _logger.LogDebug("RunReadOnlyQueryAsync: sql={Sql}", sql);
        return await ExecuteQueryAsync(sql, [], ct);
    }

    // -----------------------------------------------------------------------
    // 共通ヘルパ
    // -----------------------------------------------------------------------
    private async Task<SqlQueryResult> ExecuteQueryAsync(
        string sql,
        IEnumerable<SqlParameter> parameters,
        CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new SqlCommand(sql, conn);
        cmd.CommandTimeout = 30;

        foreach (var p in parameters)
            cmd.Parameters.Add(p);

        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var columns = new List<string>(reader.FieldCount);
        for (int i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));

        var rows = new List<IReadOnlyList<object?>>();
        while (await reader.ReadAsync(ct))
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }

        return new SqlQueryResult(columns, rows, rows.Count);
    }
}
