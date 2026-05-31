namespace OpsContext.Agents.Agents;

// スタブ: design 04 で本実装予定。
public sealed record AgentResponse(
    string Message,
    IReadOnlyList<ToolCallResult> ToolCalls);

/// <summary>エージェントが実行したツール呼び出しの記録。右ペインのツール呼出カードに対応する。</summary>
public sealed record ToolCallResult(
    string ToolName,
    string Query,
    string ResultMarkdown);
