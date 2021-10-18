using System.Threading.Tasks;

namespace DotYou.Types.Messaging
{
    public interface IMessagingHub
    {
        Task NewChatMessageReceived(ChatMessageEnvelope message);

        Task NewChatMessageSent(ChatMessageEnvelope message);
        
        Task NewEmailReceived(Message message);
    }
}