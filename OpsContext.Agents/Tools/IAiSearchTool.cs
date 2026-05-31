using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

// plan.md「Tool 層シグネチャ」/ design 03 に従うインターフェース定義。
public interface IAiSearchTool
{
    /// <summary>
    /// opscontext-knowledge インデックスをベクトル検索する。
    /// roleFilter 非 null 時は role eq '{roleFilter}' or role eq 'All' でフィルタする。
    /// </summary>
    Task<IReadOnlyList<AiSearchHit>> SearchKnowledgeAsync(
        string query, int topK, string? roleFilter, CancellationToken ct);

    /// <summary>
    /// opscontext-context インデックスをベクトル検索する。
    /// customerCode 非 null 時は customerCode eq '{customerCode}' でフィルタする。
    /// </summary>
    Task<IReadOnlyList<AiSearchHit>> SearchDecisionLogsAsync(
        string query, string? customerCode, int topK, CancellationToken ct);

    /// <summary>
    /// ContextEntries 1件を opscontext-context インデックスに upsert してドキュメントIDを返す。
    /// ContextStoreTool の AppendDecision/AppendObservation から呼ばれる。
    /// </summary>
    Task<string> UpsertContextEntryAsync(
        string entryId, string caseId, string role, string kind,
        string customerCode, string text, CancellationToken ct);
}
