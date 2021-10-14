using System.Threading.Tasks;

namespace DotYou.Types.Messaging
{
    public interface IChatHub
    {
        Task NewChatMessageReceived(ChatMessageEnvelope message);

        Task NewChatMessageSent(ChatMessageEnvelope message);
    }
}