using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Types.Messaging;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Messaging.Email
{
    public class MessagingService : DotYouServiceBase, IMessagingService
    {
        private IMailboxService _mailbox;

        public MessagingService(DotYouContext context, ILogger<SimpleMailboxService> logger, HttpClientFactory fac) : base(context, logger, null, fac)
        {
            _mailbox = new SimpleMailboxService(context, "Messages", logger);
        }

        public IMailboxService Mailbox => _mailbox;

        public Task SendMessage(Message message)
        {
            //TODO: you have to divide the recipients into those wth YF identities and those without.

            foreach (var recipient in message.Recipients)
            {
                //TODO: this creates a lot of httpclients.  need to see how they are disposedı
                var client = base.CreateOutgoingHttpClient(recipient);
                client.SendEmail(message);
            }

            message.Folder = MessageFolder.Sent;
            Mailbox.Save(message);
            return Task.CompletedTask;
        }

        public void RouteIncomingMessage(Message message)
        {
            //TODO: later route these to the appropriate/categoriezed folder
            message.Folder = MessageFolder.Inbox;
            this.Mailbox.Save(message);

            this.Notify.NewEmailReceived(message);
        }
    }
}