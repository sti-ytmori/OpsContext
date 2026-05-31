using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

/// <summary>
/// インメモリ実装の IDataGridStore。複数テーブルを辞書で保持し、
/// アクティブテーブルへの操作を既存インターフェース経由で提供する。
/// MockContextStoreTool と同様の lock パターンを踏襲する。
/// </summary>
public sealed class MockDataGridStore : IDataGridStore
{
    // MockContextStoreTool のシード済み A商事案件と同一 caseId。
    // Data.razor の ERP 突合 observation が Chat.razor のコンテキストと統一される。
    public const string DefaultCaseId = "a1b2c3d4-0001-0000-0000-000000000001";
    private const string SeedTableId  = "grid-main-001";

    private readonly Dictionary<string, GridTable> _tables = [];
    private string _activeId = SeedTableId;
    private readonly object _lock = new();
    private bool _seeded = false;

    // -----------------------------------------------------------------------
    // 複数テーブル管理
    // -----------------------------------------------------------------------

    public IReadOnlyList<GridTable> ListTables()
    {
        lock (_lock)
            return _tables.Values.Select(Snapshot).ToList().AsReadOnly();
    }

    public GridTable? GetTable(string id)
    {
        lock (_lock)
            return _tables.TryGetValue(id, out var t) ? Snapshot(t) : null;
    }

    public string CreateTable(string name)
    {
        lock (_lock)
        {
            var table = new GridTable { Name = name };
            _tables[table.Id] = table;
            return table.Id;
        }
    }

    public void SetActiveTable(string id)
    {
        lock (_lock)
        {
            if (_tables.ContainsKey(id))
                _activeId = id;
        }
    }

    public void DeleteTable(string id)
    {
        lock (_lock)
        {
            // 最後の1個は削除不可
            if (_tables.Count <= 1) return;
            _tables.Remove(id);
            // アクティブが消えた場合は最初のテーブルを選ぶ
            if (_activeId == id)
                _activeId = _tables.Keys.First();
        }
    }

    // -----------------------------------------------------------------------
    // アクティブテーブルへの操作
    // -----------------------------------------------------------------------

    public string GetDefaultCaseId() => DefaultCaseId;

    public GridTable GetActiveTable()
    {
        lock (_lock)
            return Snapshot(ActiveTable());
    }

    public Task SeedSampleAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_seeded) return Task.CompletedTask;
            _seeded = true;

            var table = new GridTable
            {
                Id   = SeedTableId,
                Name = "見積明細",
                Columns =
                [
                    new("product_code", "品番",           GridColumnType.Text,   "ERP品目マスタの主キー。英大文字+ハイフン+3桁数字の形式（例: 弁P-101）。"),
                    new("product_name", "品名",           GridColumnType.Text,   "品目マスタに登録された正式名称。見積書や発注書に印字される。"),
                    new("qty",          "数量",           GridColumnType.Number, "発注・見積の数量（単位は品目マスタで管理。通常は「個」）。"),
                    new("unit_price",   "単価（円）",     GridColumnType.Number, "税抜き単価（円）。ERP価格マスタの直近単価と突合する。"),
                    new("req_date",     "希望納期",       GridColumnType.Date,   "顧客が要望する納入希望日。yyyy-MM-dd 形式で記録する。"),
                    new("verdict",      "判定",           GridColumnType.Text,   "AIによるERP突合結果。OK / Warning / NG の3値。"),
                    new("risk",         "リスク",         GridColumnType.Text,   "AIが検出したリスク内容の要約（納期遅延・単価乖離・在庫不足など）。"),
                    new("recommend",    "推奨アクション", GridColumnType.Text,   "担当者が取るべき次のアクション案（AIが提案）。"),
                ],
                Rows =
                [
                    MakeRow([
                        ("product_code", (object?)"弁P-101"),
                        ("product_name", "弁P-101（標準型）"),
                        ("qty",          150),
                        ("unit_price",   65000),
                        ("req_date",     "2026-06-30"),
                        ("verdict",      ""),
                        ("risk",         ""),
                        ("recommend",    ""),
                    ]),
                    MakeRow([
                        ("product_code", (object?)"P-202"),
                        ("product_name", "ポンプユニットP-202"),
                        ("qty",          30),
                        ("unit_price",   120000),
                        ("req_date",     "2026-07-15"),
                        ("verdict",      ""),
                        ("risk",         ""),
                        ("recommend",    ""),
                    ]),
                    MakeRow([
                        ("product_code", (object?)"P-303"),
                        ("product_name", "継手セットP-303"),
                        ("qty",          500),
                        ("unit_price",   3500),
                        ("req_date",     "2026-06-20"),
                        ("verdict",      ""),
                        ("risk",         ""),
                        ("recommend",    ""),
                    ]),
                ]
            };
            _tables[table.Id] = table;

            // ─── テーブル2: 案件一覧 ────────────────────────────────────
            var cases = new GridTable
            {
                Id   = "grid-cases-001",
                Name = "案件一覧",
                Columns =
                [
                    new("case_id",       "案件ID",     GridColumnType.Text,   "システム生成の案件識別子。チャット画面の caseId と対応する。"),
                    new("title",         "案件名",     GridColumnType.Text,   "案件の概要タイトル。顧客名＋内容で表記する。"),
                    new("customer_code", "顧客コード", GridColumnType.Text,   "ERP得意先マスタの顧客コード（例: A001）。"),
                    new("status",        "ステータス", GridColumnType.Text,   "進行中 / 保留 / 完了 の3値。"),
                    new("created_at",    "登録日",     GridColumnType.Date,   "案件を最初に登録した日付。"),
                    new("updated_at",    "最終更新日", GridColumnType.Date,   "最後にコンテキストが更新された日付。"),
                ],
                Rows =
                [
                    MakeRow([
                        ("case_id",       (object?)"a1b2c3d4-0001-0000-0000-000000000001"),
                        ("title",         "A商事 弁P-101 150個受注検討"),
                        ("customer_code", "A001"),
                        ("status",        "進行中"),
                        ("created_at",    "2026-05-29"),
                        ("updated_at",    "2026-05-30"),
                    ]),
                    MakeRow([
                        ("case_id",       (object?)"b2c3d4e5-0002-0000-0000-000000000002"),
                        ("title",         "B産業 緊急調達案件"),
                        ("customer_code", "B002"),
                        ("status",        "保留"),
                        ("created_at",    "2026-05-26"),
                        ("updated_at",    "2026-05-27"),
                    ]),
                    MakeRow([
                        ("case_id",       (object?)"c3d4e5f6-0003-0000-0000-000000000003"),
                        ("title",         "C製造 定期発注 Q1"),
                        ("customer_code", "C003"),
                        ("status",        "完了"),
                        ("created_at",    "2026-05-21"),
                        ("updated_at",    "2026-05-24"),
                    ]),
                ]
            };
            _tables[cases.Id] = cases;

            _activeId = table.Id;
        }
        return Task.CompletedTask;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _tables.Clear();
            _activeId = SeedTableId;
            _seeded   = false;
        }
    }

    public void UpdateCell(string rowId, string columnKey, object? value)
    {
        lock (_lock)
        {
            var row = ActiveTable().Rows.FirstOrDefault(r => r.RowId == rowId);
            if (row is null) return;
            row.Cells[columnKey] = value;
        }
    }

    public string AddRow(Dictionary<string, object?>? initialCells = null)
    {
        lock (_lock)
        {
            var t = ActiveTable();
            var cells = new Dictionary<string, object?>();
            foreach (var col in t.Columns)
                cells[col.Key] = null;

            if (initialCells is not null)
                foreach (var (k, v) in initialCells)
                    cells[k] = v;

            var row = new GridRow { Cells = cells };
            t.Rows.Add(row);
            return row.RowId;
        }
    }

    public void DeleteRow(string rowId)
    {
        lock (_lock)
            ActiveTable().Rows.RemoveAll(r => r.RowId == rowId);
    }

    public void AddColumn(GridColumn column)
    {
        lock (_lock)
        {
            var t = ActiveTable();
            if (t.Columns.Any(c => c.Key == column.Key)) return;
            t.Columns.Add(column);
            foreach (var row in t.Rows)
                row.Cells.TryAdd(column.Key, null);
        }
    }

    public void ReplaceTable(GridTable table)
    {
        lock (_lock)
        {
            // アクティブテーブルを差し替える
            _tables[_activeId] = table;
            _seeded = true;
        }
    }

    public void DeleteColumn(string columnKey)
    {
        lock (_lock)
        {
            var t = ActiveTable();
            t.Columns.RemoveAll(c => c.Key == columnKey);
            foreach (var row in t.Rows)
                row.Cells.Remove(columnKey);
        }
    }

    public void UpdateColumnDescription(string columnKey, string description)
    {
        lock (_lock)
        {
            var t   = ActiveTable();
            var idx = t.Columns.FindIndex(c => c.Key == columnKey);
            if (idx < 0) return;
            t.Columns[idx] = t.Columns[idx] with { Description = description };
        }
    }

    // -----------------------------------------------------------------------
    // ヘルパ
    // -----------------------------------------------------------------------

    /// <summary>lock 内で呼ぶこと。アクティブテーブルの参照を返す。</summary>
    private GridTable ActiveTable()
    {
        if (_tables.TryGetValue(_activeId, out var t)) return t;
        // フォールバック: 最初のテーブルを使う（シード前など）
        if (_tables.Count > 0)
        {
            _activeId = _tables.Keys.First();
            return _tables[_activeId];
        }
        // 空の場合は空テーブルを作成してセット
        var empty = new GridTable { Id = _activeId };
        _tables[_activeId] = empty;
        return empty;
    }

    /// <summary>テーブルのディープコピー（スナップショット）を生成する。</summary>
    private static GridTable Snapshot(GridTable src) => new()
    {
        Id      = src.Id,
        Name    = src.Name,
        Columns = [.. src.Columns],
        Rows    = src.Rows.Select(r => new GridRow
        {
            RowId = r.RowId,
            Cells = new Dictionary<string, object?>(r.Cells)
        }).ToList()
    };

    private static GridRow MakeRow(
        IEnumerable<(string Key, object? Value)> cells)
    {
        var row = new GridRow();
        foreach (var (key, value) in cells)
            row.Cells[key] = value;
        return row;
    }
}
