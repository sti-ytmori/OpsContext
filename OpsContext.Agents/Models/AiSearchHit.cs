namespace OpsContext.Agents.Models;

// design 03 の AiSearchHit 型定義。
public record AiSearchHit
{
    public double Score { get; init; }
    public string Content { get; init; } = "";
    public string Id { get; init; } = "";
    public Dictionary<string, string> Metadata { get; init; } = new();
}
