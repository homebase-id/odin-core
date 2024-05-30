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
    public class PeerOutboxProcessorAsync(
        IPeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessorAsync> logger,
        PushNotificationService pushNotificationService,
        IAppRegistrationService appRegistrationService,
        FileSystemResolver fileSystemResolver,
        IMediator mediator,
        TenantSystemStorage tenantSystemStorage)
    {
        public async Task StartOutboxProcessingAsync(IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = await peerOutbox.GetNextItem(cn);

            while (item != null)
            {
                _ = ProcessItem(item, odinContext);
                item = await peerOutbox.GetNextItem(cn);
            }
        }

        /// <summary>
        /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
        /// </summary>
        private async Task<OutboxProcessingResult> ProcessItem(OutboxItem item, IOdinContext odinContext)
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
                    result = await SendFileOutboxItem(item, odinContext, connection);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }


            return result;
        }

        private async Task<OutboxProcessingResult> SendFileOutboxItem(OutboxItem item, IOdinContext odinContext, DatabaseConnection cn)
        {
            var worker = new SendFileOutboxWorkerAsync(item,
                fileSystemResolver,
                // logger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory,
                mediator
                //,jobManager
                );

            var result = await worker.Send(odinContext, cn);
            
            return result;
        }

        private async Task<OutboxProcessingResult> SendPushNotification(OutboxItem item, IOdinContext odinContext, DatabaseConnection cn)
        {
            var worker = new SendPushNotificationOutboxWorker(item,
                appRegistrationService,
                pushNotificationService,
                peerOutbox);

            return await worker.Send(odinContext, cn);
        }
    }
}