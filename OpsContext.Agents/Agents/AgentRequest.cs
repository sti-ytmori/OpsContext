namespace OpsContext.Agents.Agents;

// design 04: AgentRequest — Orchestrator / ロールエージェントへの入力 DTO。
// History は Blazor 側のチャット履歴。ContextSummary は ReadCaseContextAsync の結果。
// RolePrompt / PersonalPrompt はシステムプロンプトへの追記指示（DB から取得して渡す）。
public sealed record AgentRequest(
    string CaseId,
    string Role,
    string UserMessage,
    IReadOnlyList<(string Speaker, string Message)> History,
    string? ContextSummary = null,
    string? RolePrompt = null,
    string? PersonalPrompt = null);
