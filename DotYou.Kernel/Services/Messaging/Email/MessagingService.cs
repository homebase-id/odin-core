using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Types;
using DotYou.Types.Messaging;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Messaging.Email
{
    public class MessagingService : DotYouServiceBase, IMessagingService
    {
        private IMessageFolderService _inbox;
        private IMessageFolderService _drafts;
        private IMessageFolderService _sentItems;

        public MessagingService(DotYouContext context, ILogger<SimpleMessageFolderService> logger) : base(context, logger, null)
        {
            _inbox = new SimpleMessageFolderService(context, "Inbox", logger);
            _sentItems = new SimpleMessageFolderService(context, "SentItems", logger);
            _drafts = new SimpleMessageFolderService(context, "Drafts", logger);
        }

        public IMessageFolderService Inbox => _inbox;

        public IMessageFolderService Drafts => _drafts;

        public IMessageFolderService SentItems => _sentItems;

        public Task SendMessage(Message message)
        {
            //TODO: you have to divide the recipients into those wth YF identities and those without.
            var client = new HttpClient();

            foreach (var recipient in message.Recipients)
            {
                var b = new UriBuilder();
                b.Path = "api/incoming/messages";
                b.Host = recipient;
                b.Scheme = "https";

                //Note: the casting is required to ensure the fields are sent. (I don't
                // know why, perhaps it's something to do with the json formatting)
                client.PostAsJsonAsync<Message>(b.Uri, (Message)message);
            }
            
            return Task.CompletedTask;
        }

        public void RouteIncomingMessage(Message message)
        {
            //later route these to the appropriate/categoriezed folder
            this.Inbox.Save(message);

            this.Notify.NewEmailReceived(message);
        }
    }
}