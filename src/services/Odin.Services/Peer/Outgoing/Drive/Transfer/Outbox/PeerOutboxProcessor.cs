using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.SQLite;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
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
        FileSystemResolver fileSystemResolver,
        IMediator mediator,
        IJobManager jobManager,
        TenantSystemStorage tenantSystemStorage)
    {
        public async Task StartOutboxProcessing(IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = await peerOutbox.GetNextItem(cn);

            while (item != null)
            {
                await ProcessItem(item, odinContext, tryDeleteTransient: true);
                item = await peerOutbox.GetNextItem(cn);
            }
        }

        public async Task StartOutboxProcessingAsync(IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = await peerOutbox.GetNextItem(cn);

            while (item != null)
            {
                _ = ProcessItem(item, odinContext, tryDeleteTransient: true);
                item = await peerOutbox.GetNextItem(cn);
            }
        }

        /// <summary>
        /// Processes the set of items as a whole; failures get enqueued to the outbox
        /// </summary>
        public async Task<List<OutboxProcessingResult>> ProcessItemsSync(IEnumerable<OutboxItem> items, IOdinContext odinContext, DatabaseConnection cn)
        {
            var results = new List<OutboxProcessingResult>();
            var stack = new Stack<OutboxItem>(items);
            while (stack.Count > 0)
            {
                var item = stack.Pop();

                var result = await ProcessItem(item, odinContext, tryDeleteTransient: false);
                results.Add(result);
                if (result.TransferResult != TransferResult.Success)
                {
                    //enqueue into the outbox since it was never added before
                    await peerOutbox.Add(item, cn, useUpsert: true); //useUpsert just in-case
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
        private async Task<OutboxProcessingResult> ProcessItem(OutboxItem item, IOdinContext odinContext, bool tryDeleteTransient)
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", item.Type);

            using var connection = tenantSystemStorage.CreateConnection();

            OutboxProcessingResult result;
            switch (item.Type)
            {
                case OutboxItemType.PushNotification:
                    result = await SendPushNotification(item, odinContext, connection);
                    break;

                case OutboxItemType.File:
                    result = await SendFileOutboxItem(item, odinContext, tryDeleteTransient, connection);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }


            return result;
        }

        private async Task<OutboxProcessingResult> SendFileOutboxItem(OutboxItem item, IOdinContext odinContext, bool tryDeleteTransient, DatabaseConnection cn)
        {
            var worker = new SendFileOutboxWorker(item,
                fileSystemResolver,
                logger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory,
                mediator,
                jobManager);

            var result = await worker.Send(odinContext, tryDeleteTransient, cn);

            return result;
        }

        private async Task<OutboxProcessingResult> SendPushNotification(OutboxItem item, IOdinContext odinContext, DatabaseConnection cn)
        {
            var worker = new SendPushNotificationOutboxWorker(item,
                appRegistrationService,
                pushNotificationService,
                logger,
                peerOutbox);

            return await worker.Send(odinContext, cn);
        }
    }
}