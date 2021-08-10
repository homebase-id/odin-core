using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using DotYou.IdentityRegistry;
using DotYou.Kernel.HttpClient;
using DotYou.Kernel.Services.Circle;
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
        private readonly IProfileService _profileService;
        private readonly ICircleNetworkService _cns;

        public ChatService(DotYouContext context, ILogger<ChatService> logger, IHubContext<NotificationHub, INotificationHub> hub, DotYouHttpClientFactory fac, IProfileService profileService, ICircleNetworkService cns) : base(context, logger, hub, fac)
        {
            _profileService = profileService;
            _cns = cns;
        }

        public async Task<PagedResult<AvailabilityStatus>> GetAvailableContacts(PageOptions options)
        {
            //TODO: this needs to hold a cache of the availability status
            // when checking updates, it needs to examine the Updated timestamp
            // of each status to see if should re-query the DI

            
            var connections = await _cns.GetConnections(options);
            
            var bag = new ConcurrentBag<AvailabilityStatus>();
            
            var tasks = connections.Results.Select(async connectionInfo =>
            {
                var client = base.CreatePerimeterHttpClient(connectionInfo.Id);
                var response = await client.GetAvailability();
            
                var canChat = response.IsSuccessStatusCode && response.Content == true;
            
                var av = new AvailabilityStatus
                {
                    IsChatAvailable = canChat,
                    Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    DotYouId =  connectionInfo.Id,
                    DisplayName = connectionInfo.Id, //TODO: get the name information from the profile service
                    StatusMessage = ""
                };
            
                bag.Add(av);
            });
            
            await Task.WhenAll(tasks);
            
            var availabilityPage = new PagedResult<AvailabilityStatus>(options, connections.TotalPages, bag.ToList());
            return availabilityPage;
        }

        public async Task<bool> SendMessage(ChatMessageEnvelope message)
        {
            //look up recipient's public key from contacts
            var contact = await _profileService.Get(message.Recipient);

            if (null == contact || ValidationUtil.IsNullEmptyOrWhitespace(contact.PublicKeyCertificate))
            {
                throw new MessageSendException($"Cannot find public key certificate for {message.Recipient}");
            }

            message.SenderDotYouId = this.Context.HostDotYouId;
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

                var recent = new RecentChatMessageHeader()
                {
                    DotYouId = message.Recipient,
                    Body = message.Body,
                    Timestamp = message.ReceivedTimestampMilliseconds
                };

                WithTenantStorage<RecentChatMessageHeader>(RECENT_CHAT_MESSAGES_HISTORY, s => s.Save(recent));

                await this.Notify.NewChatMessageSent(message);
            }

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ReceiveIncomingMessage(ChatMessageEnvelope message)
        {
            //TODO: add validation - like not allowing empty messages
            
            string collection = GetChatStoragePath(message.SenderDotYouId);
            WithTenantStorage<ChatMessageEnvelope>(collection, s => s.Save(message));

            var recent = new RecentChatMessageHeader()
            {
                DotYouId = message.SenderDotYouId,
                Body = message.Body,
                Timestamp = message.ReceivedTimestampMilliseconds
            };

            WithTenantStorage<RecentChatMessageHeader>(RECENT_CHAT_MESSAGES_HISTORY, s => s.Save(recent));

            await this.Notify.NewChatMessageReceived(message);

            return true;
        }

        public async Task<PagedResult<RecentChatMessageHeader>> GetRecentMessages(PageOptions pageOptions)
        {
            var page = await WithTenantStorageReturnList<RecentChatMessageHeader>(RECENT_CHAT_MESSAGES_HISTORY, s => s.GetList(pageOptions, ListSortDirection.Descending, sortKey => sortKey.Timestamp));

            //HACK:  need to redesign the storage of chat and/or recent messages
            var grouping = page.Results.GroupBy(h => h.DotYouId, StringComparer.InvariantCultureIgnoreCase);
            var list = grouping.Select(g =>
            {
                var mostRecentMessage = g.OrderByDescending(m => m.Timestamp).First();
                var header = new RecentChatMessageHeader();
                header.DotYouId = (DotYouIdentity) g.Key;
                header.Body = mostRecentMessage.Body;
                header.Timestamp = mostRecentMessage.Timestamp;

                return header;
            }).ToList();

            return new PagedResult<RecentChatMessageHeader>(page.Request, 1, list);
        }

        public async Task<DateRangePagedResult<ChatMessageEnvelope>> GetHistory(DotYouIdentity dotYouId, Int64 startDateTimeOffsetSeconds, Int64 endDateTimeOffsetSeconds, PageOptions pageOptions)
        {
            string collection = GetChatStoragePath(dotYouId);
            var page = await WithTenantStorageReturnList<ChatMessageEnvelope>(collection, s =>
            {
                Expression<Func<ChatMessageEnvelope, bool>> predicate = p => (p.ReceivedTimestampMilliseconds >= startDateTimeOffsetSeconds &&
                                                                              p.ReceivedTimestampMilliseconds <= endDateTimeOffsetSeconds);

                Expression<Func<ChatMessageEnvelope, long>> sortKeySelector = key => key.ReceivedTimestampMilliseconds;

                return s.Find(predicate, ListSortDirection.Ascending, sortKeySelector, pageOptions);
            });

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