using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Dawn;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Refit;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Identity;
using Youverse.Core.Services.Profile;
using Youverse.Core.Services.Storage;
using Youverse.Core.Util;

namespace Youverse.Services.Messaging.Chat
{
    public class ChatService : DotYouServiceBase, IChatService
    {
        private const string ChatMessageStorageCollection = "chat";
        private const string RecentChatMessagesHistoryCollection = "recent_messages";

        private readonly IProfileService _profileService;
        private readonly ICircleNetworkService _cns;
        private readonly IHubContext<MessagingHub, IMessagingHub> _messagingHub;
        private readonly IStorageService _storageService;

        public ChatService(DotYouContext context, ILogger<ChatService> logger, DotYouHttpClientFactory fac, IProfileService profileService, ICircleNetworkService cns, IHubContext<MessagingHub, IMessagingHub> messagingHub, IStorageService storageService) : base(context, logger, null, fac)
        {
            _profileService = profileService;
            _cns = cns;
            _messagingHub = messagingHub;
            _storageService = storageService;
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
                var client = base.CreatePerimeterHttpClient<IMessagingPerimeterHttpClient>(connectionInfo.DotYouId);
                var response = await client.GetAvailability();

                var canChat = response.IsSuccessStatusCode && response.Content == true;

                var av = new AvailabilityStatus
                {
                    IsChatAvailable = canChat,
                    Updated = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    DotYouId = connectionInfo.DotYouId,
                    DisplayName = connectionInfo.DotYouId, //TODO: get the name information from the profile service
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
            Console.BackgroundColor = ConsoleColor.DarkCyan;
            Console.ForegroundColor = ConsoleColor.White;

            //look up recipient's public key from contacts
            var contact = await _profileService.Get(message.Recipient);

            if (null == contact || ValidationUtil.IsNullEmptyOrWhitespace(contact.PublicKeyCertificate))
            {
                throw new MessageSendException($"Cannot find public key certificate for {message.Recipient}");
            }

            message.SenderDotYouId = this.Context.HostDotYouId;
            message.ReceivedTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            Console.WriteLine($"Message being sent from {this.Context.HostDotYouId} to {message.Recipient}");

            var encryptedMessage = new ChatMessageEnvelope()
            {
                Id = message.Id,
                Recipient = message.Recipient,
                ReceivedTimestampMilliseconds = message.ReceivedTimestampMilliseconds,
                //SentTimestampMilliseconds = message.SentTimestampMilliseconds,
                SenderDotYouId = message.SenderDotYouId,
                SenderPublicKeyCertificate = message.SenderPublicKeyCertificate,
                Body = message.Body
            };

            Logger.LogDebug($"Media Id: {message.MediaId}");

            ApiResponse<NoResultResponse> response;
            var client = this.CreatePerimeterHttpClient<IMessagingPerimeterHttpClient>(message.Recipient);
            if (message.MediaId == Guid.Empty)
            {
                response = await client.DeliverChatMessage(encryptedMessage, null, null);
            }
            else
            {
                MediaMetaData metaData = await _storageService.GetMetaData(message.MediaId);
                
                if (metaData == null)
                {
                    Logger.LogWarning($"SendMessage -> Meta data missing for [{message.MediaId}]");
                    response = await client.DeliverChatMessage(encryptedMessage, null, null);
                }
                else
                {
                    await using var mediaStream = await _storageService.GetMediaStream(message.MediaId);
                    response = await client.DeliverChatMessage(encryptedMessage, metaData, mediaStream);
                }
            }

            //var response = await client.DeliverChatMessage(encryptedMessage, metaData, bytes);

            if (response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Message successfully sent to {message.Recipient}");

                //upon successful delivery of the message, save our message
                WithTenantStorage<ChatMessageEnvelope>(GetChatStoragePath(message.Recipient), s => s.Save(message));

                var recent = new RecentChatMessageHeader()
                {
                    DotYouId = message.Recipient,
                    Body = message.Body,
                    Timestamp = message.ReceivedTimestampMilliseconds
                };

                WithTenantStorage<RecentChatMessageHeader>(RecentChatMessagesHistoryCollection, s => s.Save(recent));

                await this.MessagingHub.NewChatMessageSent(message);
                //Console.WriteLine($"ChatHub.NewChatMessageSent sent to {this.Context.HostDotYouId}");
            }
            else
            {
                Console.WriteLine($"Message failed to be sent to {message.Recipient}");
                Console.WriteLine($"Status code {response.StatusCode}");
            }

            Console.BackgroundColor = ConsoleColor.Black;
            Console.ForegroundColor = ConsoleColor.White;

            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ReceiveMessage(ChatMessageEnvelope envelope, MediaMetaData metaData, Stream mediaStream)
        {
            //TODO: add validation - like not allowing empty messages
            // Console.BackgroundColor = ConsoleColor.Yellow;
            // Console.ForegroundColor = ConsoleColor.Black;

            Console.WriteLine($"Message received from {envelope.SenderDotYouId} to {this.Context.HostDotYouId}");

            Console.WriteLine($"has metadata: {(metaData != null).ToString()}");
            Console.WriteLine($"metadata len: {mediaStream?.Length}");
            
            if (metaData != null && mediaStream is { Length: > 0 })
            {
                Console.WriteLine($"Message has media of type [{metaData.MimeType}] and length: {mediaStream.Length}");
                Guard.Argument(metaData.MimeType, nameof(metaData.MimeType)).NotNull("Mimetype required").NotEmpty("Mimetype required");
                envelope.MediaId = await _storageService.SaveMedia(metaData, mediaStream, giveNewId: true);
            }
            
            string collection = GetChatStoragePath(envelope.SenderDotYouId);
            WithTenantStorage<ChatMessageEnvelope>(collection, s => s.Save(envelope));

            var recent = new RecentChatMessageHeader()
            {
                DotYouId = envelope.SenderDotYouId,
                Body = envelope.Body,
                Timestamp = envelope.ReceivedTimestampMilliseconds
            };

            WithTenantStorage<RecentChatMessageHeader>(RecentChatMessagesHistoryCollection, s => s.Save(recent));

            await this.MessagingHub.NewChatMessageReceived(envelope);

            // Console.WriteLine($"ChatHub.NewChatMessageReceived sent to {this.Context.HostDotYouId}");
            // Console.BackgroundColor = ConsoleColor.Black;
            // Console.ForegroundColor = ConsoleColor.White;

            return true;
        }

        public async Task<PagedResult<RecentChatMessageHeader>> GetRecentMessages(PageOptions pageOptions)
        {
            var page = await WithTenantStorageReturnList<RecentChatMessageHeader>(RecentChatMessagesHistoryCollection, s => s.GetList(pageOptions, ListSortDirection.Descending, sortKey => sortKey.Timestamp));

            //HACK:  need to redesign the storage of chat and/or recent messages
            var grouping = page.Results.GroupBy(h => h.DotYouId, StringComparer.InvariantCultureIgnoreCase);
            var list = grouping.Select(g =>
            {
                var mostRecentMessage = g.OrderByDescending(m => m.Timestamp).First();
                var header = new RecentChatMessageHeader();
                header.DotYouId = (DotYouIdentity)g.Key;
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

                return s.Find(predicate, ListSortDirection.Descending, sortKeySelector, pageOptions);
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
            return $"{ChatMessageStorageCollection}_{dotYouId.Id.Replace(".", "_")}";
        }

        private IMessagingHub MessagingHub
        {
            get => _messagingHub.Clients.User(this.Context.HostDotYouId);
        }
    }
}