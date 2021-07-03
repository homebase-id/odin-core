using System;
using System.Collections.Concurrent;
using System.ComponentModel;
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
        private const string CHAT_MESSAGE_STORAGE = "chat";
        private const string RECENT_CHAT_MESSAGES_HISTORY = "recent_messages";
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

            if (null == contact || ValidationUtil.IsNullEmptyOrWhitespace(contact.PublicKeyCertificate))
            {
                throw new MessageSendException($"Cannot find public key certificate for {message.Recipient}");
            }

            message.SenderDotYouId = this.Context.DotYouId;
            message.ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            //message.SentTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            var encryptedMessage = new ChatMessageEnvelope()
            {
                Id = message.Id,
                Recipient = message.Recipient,
                ReceivedTimestampMilliseconds = message.ReceivedTimestampMilliseconds,
                //SentTimestampMilliseconds = message.SentTimestampMilliseconds,
                SenderDotYouId = message.SenderDotYouId,
                SenderPublicKeyCertificate = message.SenderPublicKeyCertificate,
                Body = Cryptography.Encrypt.UsingPublicKey(contact.PublicKeyCertificate, message.Body)
            };

            var client = this.CreatePerimeterHttpClient(message.Recipient);
            var response = await client.DeliverChatMessage(encryptedMessage);

            if (response.IsSuccessStatusCode)
            {
                //upon successful delivery of the message, save our message
                WithTenantStorage<ChatMessageEnvelope>(GetChatStoragePath(message.Recipient), s => s.Save(message));
                WithTenantStorage<RecentChatMessageHeader>(RECENT_CHAT_MESSAGES_HISTORY, s => s.Save(new RecentChatMessageHeader(message)));

                await this.Notify.NewChatMessageSent(message);
            }

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ReceiveIncomingMessage(ChatMessageEnvelope message)
        {
            string collection = GetChatStoragePath(message.SenderDotYouId);
            WithTenantStorage<ChatMessageEnvelope>(collection, s => s.Save(message));

            WithTenantStorage<RecentChatMessageHeader>(RECENT_CHAT_MESSAGES_HISTORY, s => s.Save(new RecentChatMessageHeader(message)));

            await this.Notify.NewChatMessageReceived(message);

            return true;
        }

        public async Task<PagedResult<RecentChatMessageHeader>> GetRecentMessages(PageOptions pageOptions)
        {
            var page = await WithTenantStorageReturnList<RecentChatMessageHeader>(RECENT_CHAT_MESSAGES_HISTORY, s => s.GetList(pageOptions, ListSortDirection.Descending, sortKey => sortKey.Timestamp));
            return page;
        }

        public async Task<DateRangePagedResult<ChatMessageEnvelope>> GetHistory(DotYouIdentity dotYouId, Int64 startDateTimeOffsetSeconds, Int64 endDateTimeOffsetSeconds, PageOptions pageOptions)
        {
            string collection = GetChatStoragePath(dotYouId);
            var page = await WithTenantStorageReturnList<ChatMessageEnvelope>(collection, s =>
                s.Find(p => p.ReceivedTimestampMilliseconds >= startDateTimeOffsetSeconds &&
                            p.ReceivedTimestampMilliseconds <= endDateTimeOffsetSeconds, pageOptions));

            var finalResult = new DateRangePagedResult<ChatMessageEnvelope>()
            {
                StartDateTimeOffsetSeconds = startDateTimeOffsetSeconds,
                EndDateTimeOffsetSeconds = endDateTimeOffsetSeconds,
                Request = page.Request,
                Results = page.Results,
                TotalPages = page.TotalPages
            };

            return finalResult;
        }

        private string GetChatStoragePath(DotYouIdentity dotYouId)
        {
            return $"{CHAT_MESSAGE_STORAGE}_{dotYouId.Id.Replace(".", "_")}";
        }
    }
}