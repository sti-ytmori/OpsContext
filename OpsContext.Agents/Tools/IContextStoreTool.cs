using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;
// plan.md「Tool 層シグネチャ」 / design 05 に従うインターフェース定義。
// 案件データは GridTables/GridRows で管理するため Cases テーブルは持たない。
public interface IContextStoreTool
{
    /// <summary>
    /// 案件の全エントリを返す。
    /// roleViewpoint 指定時は Role = roleViewpoint OR Kind = 'handoff' の行に絞り込む。
    /// </summary>
    Task<IReadOnlyList<ContextEntry>> ReadCaseContextAsync(
        string caseId, string? roleViewpoint, CancellationToken ct);

    /// <summary>Kind = 'decision' でエントリを追加し、EntryId を返す。</summary>
    Task<string> AppendDecisionAsync(
        string caseId, string role, string author, string text, CancellationToken ct);

    /// <summary>Kind = 'observation' でエントリを追加し、EntryId を返す。</summary>
    Task<string> AppendObservationAsync(
        string caseId, string role, string author, string text, CancellationToken ct);

    /// <summary>
    /// Kind = 'handoff' でエントリを追加し、EntryId を返す。
    /// handoff エントリは roleViewpoint フィルタに関係なく全ロールから参照可能。
    /// CuratorAgent がロール横断の文脈要約を保存するために使う。
    /// </summary>
    Task<string> AppendHandoffAsync(
        string caseId, string role, string author, string text, CancellationToken ct);

    /// <summary>
    /// 指定ロールの最新 FocusSnapshot を返す。存在しない場合は null。
    /// </summary>
    Task<string?> ReadLatestFocusSnapshotAsync(string caseId, string role, CancellationToken ct);

    /// <summary>
    /// FocusSnapshots テーブルに新規レコードを INSERT する（上書きせず追記）。
    /// </summary>
    Task InsertFocusSnapshotAsync(string caseId, string role, string summaryMd, CancellationToken ct);
}
