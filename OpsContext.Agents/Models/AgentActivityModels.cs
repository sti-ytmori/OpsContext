namespace OpsContext.Agents.Models;

// ─── エージェント実行ステップ（UI のタイムライン表示用） ──────────────────────
//   GridAgentService の自律ループが、思考 → ツール呼出 → 結果 → 編集提案を
//   1 ステップずつ IProgress<AgentActivity> で UI へ送出する。
//   同一インスタンスを Running → Done に書き換えて再通知することで、
//   1 行のカードが「実行中…」→「完了」に遷移する。

public enum ActivityKind
{
    Reasoning,  // 思考・状況整理
    ToolCall,   // 読み取り系ツールの実行
    Proposal,   // 書き込み（編集）提案の登録
    Final,      // 最終回答
    Error       // 例外発生
}

public enum ActivityStatus { Running, Done, Failed }

public sealed class AgentActivity
{
    public int Step { get; init; }
    public ActivityKind Kind { get; init; }
    public string Title { get; set; } = "";          // 例: "在庫を照会 (弁P-101)"
    public string? Detail { get; set; }              // 引数サマリなど
    public string? ResultMarkdown { get; set; }      // ツール結果（折り畳み表示）
    public ActivityStatus Status { get; set; } = ActivityStatus.Running;
    public DateTimeOffset At { get; } = DateTimeOffset.UtcNow;
}

// ─── 編集提案（人間承認待ち） ─────────────────────────────────────────────────
//   書き込み系ツールが呼ばれても Grid は変更せず PendingEdit を積むだけ。
//   実際の適用は UI で人間が承認したときだけ GridAgentService.ApplyEditAsync で行う。

public enum PendingEditKind
{
    SetCell,
    AddRow,
    AddColumn,
    DeleteRow,
    DeleteColumn,
    UpdateColumnDescription
}

public sealed class PendingEdit
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public PendingEditKind Kind { get; init; }
    public string Summary { get; init; } = "";       // 人間可読の説明
    public string? RowId { get; init; }
    public string? ColumnKey { get; init; }
    public string? ColumnLabel { get; init; }
    public GridColumnType? ColumnType { get; init; }
    public string? Description { get; init; }         // UpdateColumnDescription 用
    public string? Before { get; init; }              // 差分カード: 現在値
    public string? After { get; init; }               // 差分カード: 新しい値
    public Dictionary<string, object?>? Cells { get; init; } // AddRow 用初期値
}

// ─── GridAgentService の戻り値 ────────────────────────────────────────────────
public sealed record GridAgentResult(
    string Reply,
    IReadOnlyList<AgentActivity> Activities,
    IReadOnlyList<PendingEdit> PendingEdits);
