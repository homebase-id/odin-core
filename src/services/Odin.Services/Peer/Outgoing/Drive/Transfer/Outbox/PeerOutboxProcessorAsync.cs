using System;
using System.Threading.Tasks;
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
        IJobManager jobManager,
        ILoggerFactory loggerFactory,
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
        private async Task ProcessItem(OutboxFileItem fileItem, IOdinContext odinContext)
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", fileItem.Type);

            using var connection = tenantSystemStorage.CreateConnection();

            switch (fileItem.Type)
            {
                case OutboxItemType.PushNotification:
                    await SendPushNotification(fileItem, odinContext, connection);
                    break;

                case OutboxItemType.File:
                    await SendFileOutboxItem(fileItem, odinContext, connection);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        
        private async Task SendFileOutboxItem(OutboxFileItem fileItem, IOdinContext odinContext, DatabaseConnection cn)
        {
            var workLogger = loggerFactory.CreateLogger<SendFileOutboxWorkerAsync>();
            var worker = new SendFileOutboxWorkerAsync(fileItem,
                fileSystemResolver,
                workLogger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory,
                jobManager
            );

            await worker.Send(odinContext, cn);
        }

        private async Task SendPushNotification(OutboxFileItem fileItem, IOdinContext odinContext, DatabaseConnection cn)
        {
            var worker = new SendPushNotificationOutboxWorker(fileItem,
                appRegistrationService,
                pushNotificationService,
                peerOutbox);

            await worker.Send(odinContext, cn);
        }
    }
}