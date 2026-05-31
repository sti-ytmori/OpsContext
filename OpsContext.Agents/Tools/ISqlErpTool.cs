namespace OpsContext.Agents.Tools;

// plan.md「Tool 層シグネチャ」 / design 02 に従うインターフェース定義。
public interface ISqlErpTool
{
    /// <summary>顧客の与信枠・使用額・残額を返す。</summary>
    Task<SqlQueryResult> GetCustomerCreditAsync(string customerCode, CancellationToken ct);

    /// <summary>製品の在庫数（手持・引当・安全在庫・有効在庫）を返す。</summary>
    Task<SqlQueryResult> GetProductInventoryAsync(string productCode, CancellationToken ct);

    /// <summary>製品の生産能力を日別に返す。末尾に合計サマリ行を付加。</summary>
    Task<SqlQueryResult> GetProductionCapacityAsync(string productCode, DateOnly from, DateOnly to, CancellationToken ct);

    /// <summary>同一製品・顧客の過去受注を上位 topN 件返す。</summary>
    Task<SqlQueryResult> SearchSimilarOrdersAsync(string customerCode, string productCode, int topN, CancellationToken ct);

    /// <summary>
    /// LLM が生成した任意 SQL を実行する。
    /// SELECT-only ホワイトリストガード（<see cref="SqlReadOnlyGuard"/>）を必ず通過させること。
    /// </summary>
    Task<SqlQueryResult> RunReadOnlyQueryAsync(string sql, CancellationToken ct);
}
