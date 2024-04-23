using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PeerOutboxProcessor(
        IPeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessor> logger,
        PushNotificationService pushNotificationService,
        IAppRegistrationService appRegistrationService,
        FileSystemResolver fileSystemResolver)
    {
        public async Task StartOutboxProcessing(IOdinContext odinContext)
        {
            var item = await peerOutbox.GetNextItem();

            while (item != null)
            {
                await ProcessItem(item, odinContext);
                item = await peerOutbox.GetNextItem();
            }
        }

        public async Task<List<OutboxProcessingResult>> ProcessItemsSync(IEnumerable<OutboxItem> items, IOdinContext odinContext)
        {
            var results = new List<OutboxProcessingResult>();
            foreach (var item in items)
            {
                var result = await ProcessItem(item, odinContext);
                if (result.TransferResult != TransferResult.Success)
                {
                    //enqueue into the outbox since it was never added before
                    await peerOutbox.Add(item);
                }
            }

            return results;
        }

        /// <summary>
        /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
        /// </summary>
        private async Task<OutboxProcessingResult> ProcessItem(OutboxItem item, IOdinContext odinContext)
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", item.Type);

            OutboxProcessingResult result;
            switch (item.Type)
            {
                case OutboxItemType.PushNotification:
                    result = await SendPushNotification(item, odinContext);
                    break;

                case OutboxItemType.File:
                    result = await SendFileOutboxItem(item, odinContext);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }


            return result;
        }

        private async Task<OutboxProcessingResult> SendFileOutboxItem(OutboxItem item, IOdinContext odinContext)
        {
            var worker = new SendFileOutboxWorker(item,
                fileSystemResolver,
                logger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory);

            var result = await worker.Send(odinContext);

            return result;
        }

        private async Task<OutboxProcessingResult> SendPushNotification(OutboxItem item, IOdinContext odinContext)
        {
            var worker = new SendPushNotificationOutboxWorker(item,
                appRegistrationService,
                pushNotificationService,
                logger,
                peerOutbox);

            return await worker.Send(odinContext);
        }
    }
}