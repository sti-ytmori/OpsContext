using OpsContext.Agents.Models;

namespace OpsContext.Agents.Services;

// design 06「IHostedService + Channel 構成」。
// Blazor 側から fire-and-forget で ConversationEvent を投入するインターフェース。
public interface ICuratorQueue
{
    /// <summary>Curator バックグラウンドサービスに会話イベントを投入する（非同期・ノンブロッキング）。</summary>
    void Enqueue(ConversationEvent e);
}
