using OpsContext.Agents.Models;
using System.Threading.Channels;

namespace OpsContext.Agents.Services;

// design 06「CuratorQueue」。
// Channel<ConversationEvent> への Write ラッパー。
public sealed class CuratorQueue(ChannelWriter<ConversationEvent> writer) : ICuratorQueue
{
    public void Enqueue(ConversationEvent e)
    {
        // 容量オーバーなら黙って捨てる（fire-and-forget なのでロスは許容）
        writer.TryWrite(e);
    }
}
