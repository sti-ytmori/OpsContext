using Microsoft.Extensions.AI;

namespace OpsContext.Agents.Agents;

/// <summary>
/// エージェントのシステムプロンプトにロールプロンプト・パーソナルプロンプトを追記合成するヘルパー。
/// ベースプロンプト（ハードコード）はそのまま保ち、設定済みの追加指示だけを末尾に追記する。
/// </summary>
public static class AgentPromptComposer
{
    /// <summary>
    /// basePrompt に rolePrompt / personalPrompt を追記して返す。
    /// どちらも null または空文字の場合は basePrompt をそのまま返す。
    /// </summary>
    public static string Compose(string basePrompt, string? rolePrompt, string? personalPrompt)
    {
        var hasRole     = !string.IsNullOrWhiteSpace(rolePrompt);
        var hasPersonal = !string.IsNullOrWhiteSpace(personalPrompt);

        if (!hasRole && !hasPersonal)
            return basePrompt;

        var sb = new System.Text.StringBuilder(basePrompt);

        if (hasRole)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## 追加指示（ロール共通）");
            sb.Append(rolePrompt);
        }

        if (hasPersonal)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("## 対話相手のユーザー情報");
            sb.Append(personalPrompt);
        }

        return sb.ToString();
    }

    /// <summary>
    /// チャット履歴（Blazor 側の _history）を ChatMessage のシーケンスに変換する。
    /// History の末尾は現在のユーザーメッセージなので除外し、その前の往復を返す。
    /// </summary>
    public static IEnumerable<ChatMessage> ToHistoryMessages(
        IReadOnlyList<(string Speaker, string Message)> history)
    {
        foreach (var (speaker, msg) in history.SkipLast(1))
        {
            var role = speaker == "ユーザー" ? ChatRole.User : ChatRole.Assistant;
            yield return new ChatMessage(role, msg);
        }
    }
}
