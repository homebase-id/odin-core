using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files.Old;
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
        public async Task StartOutboxProcessing(IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = await peerOutbox.GetNextItem(cn);

            while (item != null)
            {
                await ProcessItem(item, odinContext, tryDeleteTransient: true, cn);
                item = await peerOutbox.GetNextItem(cn);
            }
        }

        public async Task<List<OutboxProcessingResult>> ProcessItemsSync(IEnumerable<OutboxFileItem> items, IOdinContext odinContext, DatabaseConnection cn)
        {
            var results = new List<OutboxProcessingResult>();
            var stack = new Stack<OutboxFileItem>(items);
            while (stack.Count > 0)
            {
                var item = stack.Pop();

                var result = await ProcessItem(item, odinContext, tryDeleteTransient: false, cn);
                results.Add(result);
                if (result.TransferResult != TransferResult.Success)
                {
                    //enqueue into the outbox since it was never added before
                    await peerOutbox.AddFileItem(item, cn, useUpsert: true); //useUpsert just in-case
                }

                //TODO: interim hack
                if (result.TransferResult == TransferResult.Success && item.IsTransientFile && stack.All(s => s.File != item.File))
                {
                    var fs = fileSystemResolver.ResolveFileSystem(item.TransferInstructionSet.FileSystemType);
                    await fs.Storage.HardDeleteLongTermFile(item.File, odinContext, cn);
                }
            }

            return results;
        }

        /// <summary>
        /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
        /// </summary>
        private async Task<OutboxProcessingResult> ProcessItem(OutboxFileItem fileItem, IOdinContext odinContext, bool tryDeleteTransient, DatabaseConnection cn)
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", fileItem.Type);

            OutboxProcessingResult result;
            switch (fileItem.Type)
            {
                case OutboxItemType.PushNotification:
                    result = await SendPushNotification(fileItem, odinContext, cn);
                    break;

                case OutboxItemType.File:
                    result = await SendFileOutboxItem(fileItem, odinContext, tryDeleteTransient, cn);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }


            return result;
        }

        private async Task<OutboxProcessingResult> SendFileOutboxItem(OutboxFileItem fileItem, IOdinContext odinContext, bool tryDeleteTransient, DatabaseConnection cn)
        {
            var worker = new SendFileOutboxWorker(fileItem,
                fileSystemResolver,
                logger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory);

            var result = await worker.Send(odinContext, tryDeleteTransient, cn);

            return result;
        }

        private async Task<OutboxProcessingResult> SendPushNotification(OutboxFileItem fileItem, IOdinContext odinContext, DatabaseConnection cn)
        {
            var worker = new SendPushNotificationOutboxWorker(fileItem,
                appRegistrationService,
                pushNotificationService,
                peerOutbox);

            await worker.Send(odinContext, cn, CancellationToken.None);
            
            return new OutboxProcessingResult
            {
                Recipient = default,
                RecipientPeerResponseCode = null,
                TransferResult = TransferResult.Success,
                File = default,
                Timestamp = 0,
                OutboxFileItem = fileItem,
                VersionTag = null
            };
        }
    }
}