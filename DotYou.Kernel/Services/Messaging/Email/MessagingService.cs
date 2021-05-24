using System;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Types.Messaging;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Messaging.Email
{
    public class MessagingService : DotYouServiceBase, IMessagingService
    {
        private IMailboxService _mailbox;
        
        public MessagingService(DotYouContext context, ILogger<MessagingService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac) : base(context, logger, hub, fac)
        {
            _mailbox = new SimpleMailboxService(context, "Messages", logger);
        }

        public IMailboxService Mailbox => _mailbox;

        public async Task SendMessage(Message message)
        {
            //TODO: you have to divide the recipients into those wth YF identities and those without.

            foreach (var recipient in message.Recipients)
            {
                //TODO: this creates a lot of httpclients.  need to see how they are disposed
                var response = await base.CreatePerimeterHttpClient(recipient).SendEmail(message);
                if (!response.Content.Success)
                {
                    //TODO: add more info
                    throw new Exception("Failed to establish connection request");
                }
            }

            message.Folder = RootMessageFolder.Sent;
            await Mailbox.Save(message);

        }

        public void RouteIncomingMessage(Message message)
        {
            //TODO: later route these to the appropriate/categoriezed folder
            message.Folder = RootMessageFolder.Inbox;
            this.Mailbox.Save(message);

            this.Notify.NewEmailReceived(message);
        }
    }
}