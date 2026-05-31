using OpsContext.Agents.Models;

namespace OpsContext.Agents.Tools;

// モックモード用 AiSearchTool。
// Azure AI Search に接続せず固定ナレッジを返す。
public sealed class MockAiSearchTool : IAiSearchTool
{
    private static readonly IReadOnlyList<AiSearchHit> KnowledgeHits =
    [
        new AiSearchHit
        {
            Id = "mock-credit-1",
            Score = 0.95,
            Content = "与信枠使用率が80%を超えた場合、分割出荷を原則とする。1回あたりの出荷量は与信残高を超えないよう調整すること。",
            Metadata = new() { ["role"] = "Accounting", ["category"] = "credit_policy" }
        },
        new AiSearchHit
        {
            Id = "mock-past-1",
            Score = 0.88,
            Content = "3年前、A商事から弁P-101を150個受注。与信枠の関係で2回に分けて納品（100個+50個）。顧客も了承済み。",
            Metadata = new() { ["role"] = "Sales", ["category"] = "past_order" }
        },
        new AiSearchHit
        {
            Id = "mock-prod-1",
            Score = 0.82,
            Content = "弁P-101の標準リードタイムは30日。緊急対応枠を使えば最短2週間での生産が可能（上長承認が必要）。",
            Metadata = new() { ["role"] = "Production", ["category"] = "production_rule" }
        },
    ];

    private static readonly IReadOnlyList<AiSearchHit> DecisionHits =
    [
        new AiSearchHit
        {
            Id = "mock-decision-1",
            Score = 0.91,
            Content = "A商事 大口受注の判断ログ: 3か月前の支払遅延歴あり。与信使用率76%。分割提案を推奨。",
            Metadata = new() { ["caseId"] = "mock-case", ["role"] = "Sales", ["kind"] = "decision", ["customerCode"] = "A001" }
        },
    ];

    public Task<IReadOnlyList<AiSearchHit>> SearchKnowledgeAsync(
        string query, int topK, string? roleFilter, CancellationToken ct)
    {
        var hits = KnowledgeHits
            .Where(h => roleFilter == null
                || h.Metadata.GetValueOrDefault("role") == roleFilter
                || h.Metadata.GetValueOrDefault("role") == "All")
            .Take(topK)
            .ToList();
        return Task.FromResult<IReadOnlyList<AiSearchHit>>(hits);
    }

    public Task<IReadOnlyList<AiSearchHit>> SearchDecisionLogsAsync(
        string query, string? customerCode, int topK, CancellationToken ct)
    {
        var hits = DecisionHits
            .Where(h => customerCode == null
                || h.Metadata.GetValueOrDefault("customerCode") == customerCode)
            .Take(topK)
            .ToList();
        return Task.FromResult<IReadOnlyList<AiSearchHit>>(hits);
    }

    public Task<string> UpsertContextEntryAsync(
        string entryId, string caseId, string role, string kind,
        string customerCode, string text, CancellationToken ct)
        => Task.FromResult(entryId);
}
