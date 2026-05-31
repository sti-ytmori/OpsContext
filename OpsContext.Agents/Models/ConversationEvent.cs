namespace OpsContext.Agents.Models;

// design 06「ConversationEvent モデル」。
// Blazor → CuratorHostedService へ fire-and-forget で渡すイベント。
public sealed record ConversationEvent(
    string CaseId,
    string Role,
    string Author,
    IReadOnlyList<(string Speaker, string Message)> RecentTurns,
    DateTimeOffset OccurredAt);
