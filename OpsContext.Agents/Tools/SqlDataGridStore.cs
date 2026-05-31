using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpsContext.Agents.Models;
using OpsContext.Agents.Options;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpsContext.Agents.Tools;

/// <summary>
/// Azure SQL 永続化実装の IDataGridStore。
/// GridTables（列定義 JSON）/ GridRows（セル辞書 JSON）の2テーブルで管理する。
/// ContextStoreTool と同じ ADO.NET パターンを踏襲。
/// _activeId（アクティブテーブルのポインタ）はインメモリ保持（UI 由来の一時状態）。
/// </summary>
public sealed class SqlDataGridStore : IDataGridStore
{
    public const string DefaultCaseId  = MockDataGridStore.DefaultCaseId;
    private const string SeedTableId   = "grid-main-001";
    private const string SeedCasesId   = "grid-cases-001";

    private readonly string _connectionString;
    private readonly ILogger<SqlDataGridStore> _logger;

    private string _activeId = SeedTableId;
    private bool _schemaEnsured = false;
    private bool _seeded = false;
    private readonly object _lock = new();

    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
        Converters                  = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public SqlDataGridStore(
        IOptions<OpsContextOptions> opts,
        ILogger<SqlDataGridStore> logger)
    {
        _connectionString = opts.Value.SqlConnectionString;
        _logger = logger;
    }

    // -----------------------------------------------------------------------
    // スキーマ自動作成（冪等: DROP なし、CREATE IF NOT EXISTS）
    // -----------------------------------------------------------------------

    private void EnsureSchema()
    {
        lock (_lock)
        {
            if (_schemaEnsured) return;
            _schemaEnsured = true;
        }

        const string ddl = """
            IF OBJECT_ID('GridTables','U') IS NULL
            BEGIN
                CREATE TABLE GridTables (
                    TableId     NVARCHAR(64)  NOT NULL,
                    Name        NVARCHAR(200) NOT NULL,
                    ColumnsJson NVARCHAR(MAX) NOT NULL DEFAULT '[]',
                    CreatedAt   DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
                    UpdatedAt   DATETIME2(0)  NOT NULL DEFAULT SYSUTCDATETIME(),
                    CONSTRAINT PK_GridTables PRIMARY KEY (TableId)
                );
            END

            IF OBJECT_ID('GridRows','U') IS NULL
            BEGIN
                CREATE TABLE GridRows (
                    RowId      NVARCHAR(64)  NOT NULL,
                    TableId    NVARCHAR(64)  NOT NULL,
                    OrderIndex INT           NOT NULL DEFAULT 0,
                    CellsJson  NVARCHAR(MAX) NOT NULL DEFAULT '{}',
                    CONSTRAINT PK_GridRows PRIMARY KEY (RowId),
                    CONSTRAINT FK_GridRows_GridTables FOREIGN KEY (TableId)
                        REFERENCES GridTables (TableId) ON DELETE CASCADE
                );
                CREATE INDEX IX_GridRows_TableId ON GridRows (TableId);
            END
            """;

        using var conn = OpenConnection();
        using var cmd  = new SqlCommand(ddl, conn) { CommandTimeout = 30 };
        cmd.ExecuteNonQuery();
        _logger.LogInformation("SqlDataGridStore: schema ensured");
    }

    // -----------------------------------------------------------------------
    // IDataGridStore — 複数テーブル管理
    // -----------------------------------------------------------------------

    public IReadOnlyList<GridTable> ListTables()
    {
        const string sql = "SELECT TableId, Name, ColumnsJson FROM GridTables ORDER BY CreatedAt";

        using var conn   = OpenConnection();
        using var cmd    = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        using var reader = cmd.ExecuteReader();

        var tables = new List<(string Id, string Name, string ColJson)>();
        while (reader.Read())
            tables.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        reader.Close();

        return tables.Select(t =>
        {
            var rows = LoadRows(conn, t.Id);
            return BuildTable(t.Id, t.Name, t.ColJson, rows);
        }).ToList().AsReadOnly();
    }

    public GridTable? GetTable(string id)
    {
        const string sql = "SELECT TableId, Name, ColumnsJson FROM GridTables WHERE TableId = @Id";

        using var conn   = OpenConnection();
        using var cmd    = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        using var reader = cmd.ExecuteReader();

        if (!reader.Read()) return null;
        var (tid, name, colJson) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        reader.Close();

        var rows = LoadRows(conn, tid);
        return BuildTable(tid, name, colJson, rows);
    }

    public string CreateTable(string name)
    {
        var id = Guid.NewGuid().ToString();
        const string sql = """
            INSERT INTO GridTables (TableId, Name, ColumnsJson)
            VALUES (@Id, @Name, '[]')
            """;

        using var conn = OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Id",   id));
        cmd.Parameters.Add(new SqlParameter("@Name", name));
        cmd.ExecuteNonQuery();

        return id;
    }

    public void SetActiveTable(string id)
    {
        const string sql = "SELECT COUNT(1) FROM GridTables WHERE TableId = @Id";
        using var conn = OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        var count = (int)cmd.ExecuteScalar();
        if (count > 0)
            lock (_lock) { _activeId = id; }
    }

    public void DeleteTable(string id)
    {
        // 最後の1件は削除しない
        const string countSql = "SELECT COUNT(1) FROM GridTables";
        using var conn = OpenConnection();
        using var countCmd = new SqlCommand(countSql, conn) { CommandTimeout = 30 };
        if ((int)countCmd.ExecuteScalar() <= 1) return;

        const string sql = "DELETE FROM GridTables WHERE TableId = @Id";
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Id", id));
        cmd.ExecuteNonQuery();

        lock (_lock)
        {
            if (_activeId == id)
            {
                // 別テーブルをアクティブに切替
                const string fallbackSql = "SELECT TOP 1 TableId FROM GridTables ORDER BY CreatedAt";
                using var fb = new SqlCommand(fallbackSql, conn) { CommandTimeout = 30 };
                var fallback = fb.ExecuteScalar() as string;
                _activeId = fallback ?? SeedTableId;
            }
        }
    }

    // -----------------------------------------------------------------------
    // IDataGridStore — アクティブテーブルへの操作
    // -----------------------------------------------------------------------

    public string GetDefaultCaseId() => DefaultCaseId;

    public GridTable GetActiveTable()
    {
        string activeId;
        lock (_lock) { activeId = _activeId; }
        return GetTable(activeId) ?? BuildTable(activeId, "業務データ", "[]", []);
    }

    public async Task SeedSampleAsync(CancellationToken ct = default)
    {
        EnsureSchema();

        lock (_lock)
        {
            if (_seeded) return;
            _seeded = true;
        }

        // テーブルが存在すれば（他プロセスがシード済み）スキップ
        const string checkSql = "SELECT COUNT(1) FROM GridTables";
        using var conn = OpenConnection();
        using var checkCmd = new SqlCommand(checkSql, conn) { CommandTimeout = 30 };
        if ((int)checkCmd.ExecuteScalar() > 0)
        {
            _logger.LogInformation("SqlDataGridStore: seed skipped (tables already exist)");
            return;
        }

        _logger.LogInformation("SqlDataGridStore: seeding sample data");
        SeedTable(conn, SeedTableId, "見積明細",
            columns:
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
            rows:
            [
                new() { Cells = { ["product_code"]="弁P-101",["product_name"]="弁P-101（標準型）",  ["qty"]="150",["unit_price"]="65000",  ["req_date"]="2026-06-30",["verdict"]="",["risk"]="",["recommend"]="" } },
                new() { Cells = { ["product_code"]="P-202",["product_name"]="ポンプユニットP-202",["qty"]="30", ["unit_price"]="120000", ["req_date"]="2026-07-15",["verdict"]="",["risk"]="",["recommend"]="" } },
                new() { Cells = { ["product_code"]="P-303",["product_name"]="継手セットP-303",    ["qty"]="500",["unit_price"]="3500",   ["req_date"]="2026-06-20",["verdict"]="",["risk"]="",["recommend"]="" } },
            ]);

        SeedTable(conn, SeedCasesId, "案件一覧",
            columns:
            [
                new("case_id",       "案件ID",     GridColumnType.Text, "システム生成の案件識別子。チャット画面の caseId と対応する。"),
                new("title",         "案件名",     GridColumnType.Text, "案件の概要タイトル。顧客名＋内容で表記する。"),
                new("customer_code", "顧客コード", GridColumnType.Text, "ERP得意先マスタの顧客コード（例: A001）。"),
                new("status",        "ステータス", GridColumnType.Text, "進行中 / 保留 / 完了 の3値。"),
                new("created_at",    "登録日",     GridColumnType.Date, "案件を最初に登録した日付。"),
                new("updated_at",    "最終更新日", GridColumnType.Date, "最後にコンテキストが更新された日付。"),
            ],
            rows:
            [
                new() { Cells = { ["case_id"]="a1b2c3d4-0001-0000-0000-000000000001",["title"]="A商事 弁P-101 150個受注検討",["customer_code"]="A001",["status"]="進行中",["created_at"]="2026-05-29",["updated_at"]="2026-05-30" } },
                new() { Cells = { ["case_id"]="b2c3d4e5-0002-0000-0000-000000000002",["title"]="B産業 緊急調達案件",        ["customer_code"]="B002",["status"]="保留",  ["created_at"]="2026-05-26",["updated_at"]="2026-05-27" } },
                new() { Cells = { ["case_id"]="c3d4e5f6-0003-0000-0000-000000000003",["title"]="C製造 定期発注 Q1",         ["customer_code"]="C003",["status"]="完了",  ["created_at"]="2026-05-21",["updated_at"]="2026-05-24" } },
            ]);

        lock (_lock) { _activeId = SeedTableId; }
        await Task.CompletedTask;
    }

    public void Reset()
    {
        const string sql = "DELETE FROM GridTables";
        using var conn = OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.ExecuteNonQuery();
        lock (_lock)
        {
            _seeded  = false;
            _activeId = SeedTableId;
        }
    }

    public void UpdateCell(string rowId, string columnKey, object? value)
    {
        const string selectSql = "SELECT CellsJson FROM GridRows WHERE RowId = @RowId";
        const string updateSql = "UPDATE GridRows SET CellsJson = @Json WHERE RowId = @RowId";

        using var conn = OpenConnection();

        string? raw;
        using (var cmd = new SqlCommand(selectSql, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add(new SqlParameter("@RowId", rowId));
            raw = cmd.ExecuteScalar() as string;
        }
        if (raw is null) return;

        var cells = DeserializeCells(raw);
        cells[columnKey] = value?.ToString();

        using (var cmd = new SqlCommand(updateSql, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add(new SqlParameter("@Json",  SerializeCells(cells)));
            cmd.Parameters.Add(new SqlParameter("@RowId", rowId));
            cmd.ExecuteNonQuery();
        }
        TouchTable(conn, ActiveId());
    }

    public string AddRow(Dictionary<string, object?>? initialCells = null)
    {
        var table  = GetActiveTable();
        var rowId  = Guid.NewGuid().ToString();
        var cells  = table.Columns.ToDictionary(c => c.Key, _ => (string?)null);

        if (initialCells is not null)
            foreach (var (k, v) in initialCells)
                cells[k] = v?.ToString();

        const string countSql = "SELECT ISNULL(MAX(OrderIndex)+1, 0) FROM GridRows WHERE TableId = @TableId";
        const string insertSql = """
            INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson)
            VALUES (@RowId, @TableId, @Idx, @Json)
            """;

        using var conn = OpenConnection();
        int nextIdx;
        using (var cmd = new SqlCommand(countSql, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add(new SqlParameter("@TableId", ActiveId()));
            nextIdx = (int)cmd.ExecuteScalar();
        }
        using (var cmd = new SqlCommand(insertSql, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add(new SqlParameter("@RowId",   rowId));
            cmd.Parameters.Add(new SqlParameter("@TableId", ActiveId()));
            cmd.Parameters.Add(new SqlParameter("@Idx",     nextIdx));
            cmd.Parameters.Add(new SqlParameter("@Json",    SerializeCells(cells)));
            cmd.ExecuteNonQuery();
        }
        TouchTable(conn, ActiveId());
        return rowId;
    }

    public void DeleteRow(string rowId)
    {
        const string sql = "DELETE FROM GridRows WHERE RowId = @RowId";
        using var conn = OpenConnection();
        using var cmd  = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@RowId", rowId));
        cmd.ExecuteNonQuery();
        TouchTable(conn, ActiveId());
    }

    public void AddColumn(GridColumn column)
    {
        var tid = ActiveId();
        using var conn = OpenConnection();
        var cols = LoadColumns(conn, tid);
        if (cols.Any(c => c.Key == column.Key)) return;
        cols.Add(column);
        SaveColumns(conn, tid, cols);

        // 既存行に列キーを null で補完
        var rows = LoadRows(conn, tid);
        foreach (var row in rows.Where(r => !r.Cells.ContainsKey(column.Key)))
        {
            var cells = row.Cells.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
            cells[column.Key] = null;
            UpdateRowJson(conn, row.RowId, cells);
        }
        TouchTable(conn, tid);
    }

    public void DeleteColumn(string columnKey)
    {
        var tid = ActiveId();
        using var conn = OpenConnection();
        var cols = LoadColumns(conn, tid);
        cols.RemoveAll(c => c.Key == columnKey);
        SaveColumns(conn, tid, cols);

        // 既存行からキーを除去
        var rows = LoadRows(conn, tid);
        foreach (var row in rows)
        {
            var cells = row.Cells.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
            cells.Remove(columnKey);
            UpdateRowJson(conn, row.RowId, cells);
        }
        TouchTable(conn, tid);
    }

    public void UpdateColumnDescription(string columnKey, string description)
    {
        var tid = ActiveId();
        using var conn = OpenConnection();
        var cols = LoadColumns(conn, tid);
        var idx  = cols.FindIndex(c => c.Key == columnKey);
        if (idx < 0) return;
        cols[idx] = cols[idx] with { Description = description };
        SaveColumns(conn, tid, cols);
        TouchTable(conn, tid);
    }

    public void ReplaceTable(GridTable table)
    {
        var tid = ActiveId();
        using var conn = OpenConnection();

        // 列を差し替え
        SaveColumns(conn, tid, table.Columns);

        // 行を全削除→再挿入
        using (var del = new SqlCommand("DELETE FROM GridRows WHERE TableId = @TableId", conn) { CommandTimeout = 30 })
        {
            del.Parameters.Add(new SqlParameter("@TableId", tid));
            del.ExecuteNonQuery();
        }
        for (int i = 0; i < table.Rows.Count; i++)
        {
            var row   = table.Rows[i];
            var cells = row.Cells.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
            const string ins = "INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson) VALUES (@RowId, @TableId, @Idx, @Json)";
            using var cmd = new SqlCommand(ins, conn) { CommandTimeout = 30 };
            cmd.Parameters.Add(new SqlParameter("@RowId",   row.RowId));
            cmd.Parameters.Add(new SqlParameter("@TableId", tid));
            cmd.Parameters.Add(new SqlParameter("@Idx",     i));
            cmd.Parameters.Add(new SqlParameter("@Json",    SerializeCells(cells)));
            cmd.ExecuteNonQuery();
        }
        TouchTable(conn, tid);
    }

    // -----------------------------------------------------------------------
    // ヘルパ
    // -----------------------------------------------------------------------

    private string ActiveId() { lock (_lock) return _activeId; }

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_connectionString);
        conn.Open();
        return conn;
    }

    private static void TouchTable(SqlConnection conn, string tableId)
    {
        const string sql = "UPDATE GridTables SET UpdatedAt = SYSUTCDATETIME() WHERE TableId = @Id";
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Id", tableId));
        cmd.ExecuteNonQuery();
    }

    private static List<GridColumn> LoadColumns(SqlConnection conn, string tableId)
    {
        const string sql = "SELECT ColumnsJson FROM GridTables WHERE TableId = @Id";
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Id", tableId));
        var raw = cmd.ExecuteScalar() as string ?? "[]";
        return DeserializeColumns(raw);
    }

    private static void SaveColumns(SqlConnection conn, string tableId, List<GridColumn> cols)
    {
        const string sql = "UPDATE GridTables SET ColumnsJson = @Json WHERE TableId = @Id";
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Json", SerializeColumns(cols)));
        cmd.Parameters.Add(new SqlParameter("@Id",   tableId));
        cmd.ExecuteNonQuery();
    }

    private static List<GridRow> LoadRows(SqlConnection conn, string tableId)
    {
        const string sql = "SELECT RowId, CellsJson FROM GridRows WHERE TableId = @TableId ORDER BY OrderIndex";
        using var cmd    = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@TableId", tableId));
        using var reader = cmd.ExecuteReader();

        var rows = new List<GridRow>();
        while (reader.Read())
        {
            var row   = new GridRow { RowId = reader.GetString(0) };
            var cells = DeserializeCells(reader.GetString(1));
            foreach (var (k, v) in cells)
                row.Cells[k] = v;
            rows.Add(row);
        }
        return rows;
    }

    private static void UpdateRowJson(SqlConnection conn, string rowId, Dictionary<string, string?> cells)
    {
        const string sql = "UPDATE GridRows SET CellsJson = @Json WHERE RowId = @RowId";
        using var cmd = new SqlCommand(sql, conn) { CommandTimeout = 30 };
        cmd.Parameters.Add(new SqlParameter("@Json",  SerializeCells(cells)));
        cmd.Parameters.Add(new SqlParameter("@RowId", rowId));
        cmd.ExecuteNonQuery();
    }

    private static GridTable BuildTable(string id, string name, string colJson, List<GridRow> rows) =>
        new() { Id = id, Name = name, Columns = DeserializeColumns(colJson), Rows = rows };

    // JSON シリアライズ / デシリアライズ

    private static string SerializeColumns(List<GridColumn> cols) =>
        JsonSerializer.Serialize(cols, _jsonOpts);

    private static List<GridColumn> DeserializeColumns(string json)
    {
        try
        {
            // seed.sql が "name" プロパティで格納した旧フォーマットを吸収する
            var normalized = json.Replace("\"name\":", "\"key\":");
            return JsonSerializer.Deserialize<List<GridColumn>>(normalized, _jsonOpts) ?? [];
        }
        catch { return []; }
    }

    private static string SerializeCells(Dictionary<string, string?> cells) =>
        JsonSerializer.Serialize(cells, _jsonOpts);

    private static Dictionary<string, string?> DeserializeCells(string json)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string?>>(json, _jsonOpts) ?? []; }
        catch { return []; }
    }

    // シードヘルパ

    private static void SeedTable(SqlConnection conn, string tableId, string name,
        List<GridColumn> columns, List<GridRow> rows)
    {
        const string insTbl = "INSERT INTO GridTables (TableId, Name, ColumnsJson) VALUES (@Id, @Name, @Cols)";
        using (var cmd = new SqlCommand(insTbl, conn) { CommandTimeout = 30 })
        {
            cmd.Parameters.Add(new SqlParameter("@Id",   tableId));
            cmd.Parameters.Add(new SqlParameter("@Name", name));
            cmd.Parameters.Add(new SqlParameter("@Cols", SerializeColumns(columns)));
            cmd.ExecuteNonQuery();
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var row   = rows[i];
            var cells = row.Cells.ToDictionary(kv => kv.Key, kv => kv.Value?.ToString());
            const string insRow = "INSERT INTO GridRows (RowId, TableId, OrderIndex, CellsJson) VALUES (@RowId, @TableId, @Idx, @Json)";
            using var cmd = new SqlCommand(insRow, conn) { CommandTimeout = 30 };
            cmd.Parameters.Add(new SqlParameter("@RowId",   row.RowId));
            cmd.Parameters.Add(new SqlParameter("@TableId", tableId));
            cmd.Parameters.Add(new SqlParameter("@Idx",     i));
            cmd.Parameters.Add(new SqlParameter("@Json",    SerializeCells(cells)));
            cmd.ExecuteNonQuery();
        }
    }
}
