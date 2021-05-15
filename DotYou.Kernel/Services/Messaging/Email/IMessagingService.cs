using System.Threading.Tasks;
using DotYou.Types.Messaging;

namespace DotYou.Kernel.Services.Messaging.Email
{
    public interface IMessagingService
    {
        IMessageFolderService Inbox { get; }
        
        IMessageFolderService Drafts { get; }
        
        IMessageFolderService SentItems { get; }
        
        /// <summary>
        /// Sends the specified message to all recipients
        /// </summary>
        /// <param name="message"></param>
        Task SendMessage(Message message);

        /// <summary>
        /// Routes the incoming message the correct folder
        /// </summary>
        /// <param name="message"></param>
        void RouteIncomingMessage(Message message);
    }
}