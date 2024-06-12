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
    // SEB:REVIEW this class must be a BackgroundService that is started and stopped automatically by the host
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
        // SEB:REVIEW
        // - this must include a CancellationToken to be able to stop the service
        // - DatabaseConnection should not be passed in. It should be created on demand in the method
        public async Task StartOutboxProcessingAsync(IOdinContext odinContext, DatabaseConnection cn)
        {
            var item = await peerOutbox.GetNextItem(cn);

            // SEB:REVIEW
            // - GetNextIem() can return null, meaning this function will exit prematurely
            // - The loop must exit when the CancellationToken is signaled
            while (item != null)
            {
                // SEB:REVIEW
                // - task must be added to a list so that we can do a controlled shutdown when this method is exiting
                // - consider using a semaphore to limit the number of concurrent tasks (remember CancellationToken)
                _ = ProcessItem(item, odinContext);
                item = await peerOutbox.GetNextItem(cn);
            }

            // SEB:REVIEW
            // I suggest changing the above to something like this:
            //
            // while (!cancellationToken.IsCancellationRequested)
            // {
            //     using (var cn = tenantSystemStorage.CreateConnection())
            //     {
            //         while (!cancellationToken.IsCancellationRequested && await peerOutbox.GetNextItem(cn) is { } item)
            //         {
            //             await ProcessItem(item, odinContext, cancellationToken);
            //         }
            //     }
            //
            //     if (!cancellationToken.IsCancellationRequested)
            //     {
            //         await Task.Delay(1000, cancellationToken);
            //     }
            // }
        }

        /// <summary>
        /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
        /// </summary>
        ///
        // SEB:REVIEW
        // - this must include a CancellationToken to be able to stop the service
        private async Task ProcessItem(OutboxFileItem fileItem, IOdinContext odinContext)
        {
            // SEB:REVIEW
            // since this method is not being awaited, it must have a try-catch block to deal with exceptions
            // Exceptions must be logged as errors and the swallowed. No unhandled exceptions must escape this method.

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