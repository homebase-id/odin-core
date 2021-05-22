using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Contacts;
using DotYou.Types;
using DotYou.Types.Admin;
using DotYou.Types.Messaging;
using DotYou.Types.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace DotYou.Kernel.Services.Messaging.Chat
{
    public class ChatService : DotYouServiceBase, IChatService
    {
        private const string CHAT_MESSAGE_STORAGE = "ChatMessages";
        private readonly IContactService _contactService;

        public ChatService(DotYouContext context, ILogger<ChatService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac, IContactService contactService) : base(context, logger, hub, fac)
        {
            _contactService = contactService;
        }

        public async Task<PagedResult<AvailabilityStatus>> GetAvailableContacts(PageOptions options)
        {
            //TODO: this needs to hold a cache of the availability status
            // when checking updates, it needs to examine the Updated timestamp
            // of each status to see if should re-query the DI

            var contactsPage = await _contactService.GetContacts(options, true);

            var bag = new ConcurrentBag<AvailabilityStatus>();

            var tasks = contactsPage.Results.Select(async contact =>
            {
                var client = base.CreatePerimeterHttpClient(contact.DotYouId.GetValueOrDefault());
                var response = await client.GetAvailability();

                var canChat = response.IsSuccessStatusCode && response.Content == true;

                var av = new AvailabilityStatus
                {
                    IsChatAvailable = canChat,
                    Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Contact = contact
                };

                bag.Add(av);
            });

            await Task.WhenAll(tasks);

            var availabilityPage = new PagedResult<AvailabilityStatus>(options, contactsPage.TotalPages, bag.ToList());
            return availabilityPage;
        }


        public async Task<bool> SendMessage(ChatMessageEnvelope message)
        {
            //look up recipient's public key from contacts
            var contact = await _contactService.GetByDotYouId(message.Recipient);

            if (null == contact || ValidationUtil.HasNonWhitespaceValue(contact.PublicKeyCertificate))
            {
                throw new MessageSendException($"Cannot find public key certificate for {message.Recipient}");
            }

            message.Body = Cryptography.Encrypt.UsingPublicKey(contact.PublicKeyCertificate, message.Body);

            var client = this.CreatePerimeterHttpClient(message.Recipient);

            var response = await client.SendChatMessage(message);

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ReceiveIncomingMessage(ChatMessageEnvelope message)
        {
            WithTenantStorage<ChatMessageEnvelope>(CHAT_MESSAGE_STORAGE, s => s.Save(message));

            await this.Notify.NewChatMessageReceived(message);

            return true;
        }
    }
}