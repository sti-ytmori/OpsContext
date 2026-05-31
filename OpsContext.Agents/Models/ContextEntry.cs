namespace OpsContext.Agents.Models;

// design 05「ContextEntry モデル」。
public sealed record ContextEntry(
    string EntryId,
    string CaseId,
    string Role,
    string Author,
    string Kind,         // decision / observation / question / answer / handoff
    string Text,
    string? RefSql,
    DateTimeOffset CreatedAt);
