using System.Threading.Tasks;

namespace Youverse.Services.Messaging
{
    public interface IMessagingHub
    {
        Task NewChatMessageReceived(ChatMessageEnvelope message);

        Task NewChatMessageSent(ChatMessageEnvelope message);
        
        Task NewEmailReceived(Message message);
    }
}