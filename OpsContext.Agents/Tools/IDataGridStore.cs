using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

/// <summary>
/// 業務データ Grid の永続化インターフェース。
/// モックモードではインメモリ、本番は将来 Azure SQL 実装を追加する。
/// </summary>
public interface IDataGridStore
{
    // ── 複数テーブル管理 ────────────────────────────────────────────────

    /// <summary>全テーブルのスナップショット一覧を返す。</summary>
    IReadOnlyList<GridTable> ListTables();

    /// <summary>指定 Id のテーブルスナップショットを返す。存在しない場合は null。</summary>
    GridTable? GetTable(string id);

    /// <summary>空テーブルを作成し、生成した Id を返す。</summary>
    string CreateTable(string name);

    /// <summary>指定テーブルをアクティブにする。以降の Grid 操作がこのテーブルに向く。</summary>
    void SetActiveTable(string id);

    /// <summary>指定テーブルを削除する。最後の1個は削除できない。</summary>
    void DeleteTable(string id);

    // ── アクティブテーブルへの操作 ────────────────────────────────────

    /// <summary>現在アクティブな GridTable を取得する。</summary>
    GridTable GetActiveTable();

    /// <summary>
    /// Grid 操作のコンテキスト記録に使う既定の caseId を返す。
    /// Chat.razor のシード済み案件と同じ ID を返すことで、Grid と Chat のコンテキストを統一する。
    /// </summary>
    string GetDefaultCaseId();

    /// <summary>デモ用サンプルデータをシードする（冪等）。</summary>
    Task SeedSampleAsync(CancellationToken ct = default);

    /// <summary>全データをリセットする。</summary>
    void Reset();

    /// <summary>指定セルを更新する。rowId または columnKey が存在しない場合は無視する。</summary>
    void UpdateCell(string rowId, string columnKey, object? value);

    /// <summary>新規行を追加し、生成した RowId を返す。</summary>
    string AddRow(Dictionary<string, object?>? initialCells = null);

    /// <summary>指定行を削除する。</summary>
    void DeleteRow(string rowId);

    /// <summary>新規列を追加する。既存行は既定値 null で補完される。</summary>
    void AddColumn(GridColumn column);

    /// <summary>テーブル全体を差し替える（Excel 取込など）。</summary>
    void ReplaceTable(GridTable table);

    /// <summary>指定列を削除する。列キーが存在しない場合は無視する。</summary>
    void DeleteColumn(string columnKey);

    /// <summary>指定列の説明を更新する。列キーが存在しない場合は無視する。</summary>
    void UpdateColumnDescription(string columnKey, string description);
}
