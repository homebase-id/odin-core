using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Youverse.Core.Services.Base;
using Youverse.Services.Messaging.Chat;

namespace Youverse.Services.Messaging.Email
{
    public class MessagingService : DotYouServiceBase<IMessagingService>, IMessagingService
    {
        private IMailboxService _mailbox;
        private readonly IHubContext<MessagingHub, IMessagingHub> _messagingHub;
        
        public MessagingService(DotYouContext context, ILogger<IMessagingService> logger, IHubContext<MessagingHub, IMessagingHub> messagingHub, DotYouHttpClientFactory fac) : base(context, logger, null, fac)
        {
            _mailbox = new SimpleMailboxService(context, "Messages");
            _messagingHub = messagingHub;
        }

        public IMailboxService Mailbox => _mailbox;

        public async Task SendMessage(Message message)
        {
            //TODO: you have to divide the recipients into those wth YF identities and those without.

            foreach (var recipient in message.Recipients)
            {
                //TODO: this creates a lot of httpclients.  need to see how they are disposed
                var response = await base.CreatePerimeterHttpClient<IMessagingPerimeterHttpClient>(recipient).DeliverEmail(message);
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

            var hub = _messagingHub.Clients.User(this.Context.HostDotYouId);
            hub.NewEmailReceived(message);
        }
    }
}