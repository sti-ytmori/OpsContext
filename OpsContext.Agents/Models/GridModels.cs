namespace OpsContext.Agents.Models;

// ─── Grid 列型 ────────────────────────────────────────────────────────────
public enum GridColumnType { Text, Number, Date }

// ─── Grid 列定義 ──────────────────────────────────────────────────────────
public sealed record GridColumn(string Key, string Label, GridColumnType Type, string Description = "");

// ─── Grid 行 ─────────────────────────────────────────────────────────────
public sealed class GridRow
{
    public string RowId { get; init; } = Guid.NewGuid().ToString();
    public Dictionary<string, object?> Cells { get; init; } = [];
}

// ─── Grid テーブル ────────────────────────────────────────────────────────
public sealed class GridTable
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "業務データ";
    public List<GridColumn> Columns { get; set; } = [];
    public List<GridRow> Rows { get; set; } = [];
}

// Grid に対する AI の編集は GridAgentService（自律ツールループ）が PendingEdit として提案する。
// 実行可視化・提案モデルは Models/AgentActivityModels.cs を参照。
