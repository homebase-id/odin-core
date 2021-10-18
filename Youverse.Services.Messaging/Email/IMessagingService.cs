using System.Threading.Tasks;

namespace Youverse.Services.Messaging.Email
{
    public interface IMessagingService
    {
        IMailboxService Mailbox { get; }

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