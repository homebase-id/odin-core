using System;
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
        private async Task ProcessItem(OutboxItem item, IOdinContext odinContext)
        {
            //TODO: add benchmark
            logger.LogDebug("Processing outbox item type: {type}", item.Type);

            using var connection = tenantSystemStorage.CreateConnection();

            switch (item.Type)
            {
                case OutboxItemType.PushNotification:
                    await SendPushNotification(item, odinContext, connection);
                    break;

                case OutboxItemType.File:
                    await SendFileOutboxItem(item, odinContext, connection);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task SendFileOutboxItem(OutboxItem item, IOdinContext odinContext, DatabaseConnection cn)
        {
            var workLogger = loggerFactory.CreateLogger<SendFileOutboxWorkerAsync>();
            var worker = new SendFileOutboxWorkerAsync(item,
                fileSystemResolver,
                workLogger,
                peerOutbox,
                odinConfiguration,
                odinHttpClientFactory,
                mediator,
                jobManager
            );
            
            await worker.Send(odinContext, cn);
        }

        private async Task SendPushNotification(OutboxItem item, IOdinContext odinContext, DatabaseConnection cn)
        {
            var worker = new SendPushNotificationOutboxWorker(item,
                appRegistrationService,
                pushNotificationService,
                peerOutbox);

            await worker.Send(odinContext, cn);
        }
    }
}