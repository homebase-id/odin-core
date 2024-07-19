using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite;
using Odin.Core.Tasks;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Job;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;
using Quartz;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PeerOutboxProcessorAsync(
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessorAsync> logger,
        PushNotificationService pushNotificationService,
        IAppRegistrationService appRegistrationService,
        FileSystemResolver fileSystemResolver,
        IJobManager jobManager,
        ILoggerFactory loggerFactory,
        TenantSystemStorage tenantSystemStorage,
        IHostApplicationLifetime hostApplicationLifetime,
        IForgottenTasks outstandingTasks,
        IDriveAclAuthorizationService driveAcl)
    {
        public async Task StartOutboxProcessingAsync(IOdinContext odinContext, DatabaseConnection cn)
        {
            var cancellationToken = hostApplicationLifetime.ApplicationStopping;

            var item = await peerOutbox.GetNextItem(cn);
            while (item != null && cancellationToken.IsCancellationRequested == false)
            {
                var t = ProcessItemThread(item, odinContext, cancellationToken);
                outstandingTasks.Add(t);
                item = await peerOutbox.GetNextItem(cn);
            }
        }

        /// <summary>
        /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
        /// </summary>
        private async Task ProcessItemThread(OutboxFileItem fileItem, IOdinContext odinContext, CancellationToken cancellationToken)
        {
            logger.LogDebug("Processing outbox item type: {type}", fileItem.Type);
            using var connection = tenantSystemStorage.CreateConnection();

            try
            {
                var (shouldMarkComplete, nextRun) = await ProcessItemUsingWorker(fileItem, odinContext, connection, cancellationToken);
                if (shouldMarkComplete)
                {
                    await peerOutbox.MarkComplete(fileItem.Marker, connection);

                    await CleanupIfTransientItem(fileItem, odinContext, connection);
                }
                else
                {
                    await RescheduleItem(fileItem, odinContext, nextRun, connection);
                }
            }
            catch (OperationCanceledException oce)
            {
                await RescheduleItem(fileItem, odinContext, UnixTimeUtc.Now(), connection);

                // Expected when using cancellation token
                logger.LogInformation(oce, "ProcessItem Canceled for file:{file} and recipient: {r} ", fileItem.File, fileItem.Recipient);
            }
            catch (OdinFileReadException fileReadException)
            {
                await peerOutbox.MarkComplete(fileItem.Marker, connection);

                logger.LogError(fileReadException, "Source file not found {file} item (type: {itemType})", fileItem.File, fileItem.Type);
            }
            catch (OdinOutboxProcessingException e)
            {
                await RescheduleItem(fileItem, odinContext, UnixTimeUtc.Now(), connection);

                logger.LogError(e, "An outbox worker did not handle the outbox processing exception.  Action: Marking Failure" +
                                   "item (type: {itemType}).  File:{file}\t Marker:{marker}",
                    fileItem.Type,
                    fileItem.File,
                    fileItem.Marker);
            }
            catch (Exception e)
            {
                await RescheduleItem(fileItem, odinContext, UnixTimeUtc.Now(), connection);

                logger.LogError(e, "Unhandled exception occured while processing an outbox.  Action: Marking Failure." +
                                   "item (type: {itemType}).  File:{file}\t Marker:{marker}",
                    fileItem.Type,
                    fileItem.File,
                    fileItem.Marker);
            }
        }

        private async Task CleanupIfTransientItem(OutboxFileItem fileItem, IOdinContext odinContext, DatabaseConnection connection)
        {
            try
            {
                if (fileItem.State.IsTransientFile)
                {
                    var fs = fileSystemResolver.ResolveFileSystem(fileItem.State.TransferInstructionSet.FileSystemType);

                    await PerformanceCounter.MeasureExecutionTime("Outbox CleanupIfTransientFile",
                        async () =>
                        {
                            // Try to clean up the transient file
                            if (!await peerOutbox.HasOutboxFileItem(fileItem, connection))
                            {
                                logger.LogDebug("File was transient and all other outbox records sent; deleting");
                                await fs.Storage.HardDeleteLongTermFile(fileItem.File, odinContext, connection);
                            }
                        });
                }
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to clean up transient file {file}; record is already marked complete", fileItem.File);
            }
        }

        private async Task RescheduleItem(OutboxFileItem fileItem, IOdinContext odinContext, UnixTimeUtc nextRun, DatabaseConnection connection)
        {
            if (fileItem.AttemptCount > odinConfiguration.Host.PeerOperationMaxAttempts)
            {
                await peerOutbox.MarkComplete(fileItem.Marker, connection);
                logger.LogInformation(
                    "Outbox: item of type {type} and file {file} failed too many times (attempts: {attempts}) to send.  Action: Marking Complete",
                    fileItem.Type,
                    fileItem.File,
                    fileItem.AttemptCount);
                return;
            }

            await peerOutbox.MarkFailure(fileItem.Marker, nextRun, connection);

            try
            {
                await jobManager.Schedule<ProcessOutboxJob>(new ProcessOutboxSchedule(odinContext.Tenant, nextRun));
                logger.LogDebug("Scheduled re-run. NextRunTime (popStamp:{nextRunTime})", nextRun);
            }
            catch (JobPersistenceException e)
            {
                logger.LogError(e, "Job manager failed to reschedule outbox item");
            }
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> ProcessItemUsingWorker(OutboxFileItem fileItem, IOdinContext odinContext,
            DatabaseConnection connection,
            CancellationToken cancellationToken)
        {
            switch (fileItem.Type)
            {
                case OutboxItemType.PushNotification:
                    return await SendPushNotification(fileItem, odinContext, connection, cancellationToken);

                case OutboxItemType.File:
                    return await SendFileOutboxItem(fileItem, odinContext, connection, cancellationToken);

                case OutboxItemType.UnencryptedFeedItem:
                    return await SendUnencryptedFeedItem(fileItem, odinContext, connection, cancellationToken);

                case OutboxItemType.DeleteRemoteFile:
                    return await SendDeleteFileRequest(fileItem, odinContext, connection, cancellationToken);

                case OutboxItemType.ReadReceipt:
                    return await SendReadReceipt(fileItem, odinContext, connection, cancellationToken);

                case OutboxItemType.PayloadUpdate:
                    return await SendPayload(fileItem, odinContext, connection, cancellationToken);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendPayload(OutboxFileItem fileItem, IOdinContext odinContext,
            DatabaseConnection connection,
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendPayloadOutboxWorker>();
            var worker = new SendPayloadOutboxWorker(fileItem, fileSystemResolver, workLogger, odinConfiguration, odinHttpClientFactory);
            return await worker.Send(odinContext, connection, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendReadReceipt(OutboxFileItem fileItem, IOdinContext odinContext,
            DatabaseConnection connection,
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendReadReceiptOutboxWorker>();
            var worker = new SendReadReceiptOutboxWorker(fileItem, workLogger, odinHttpClientFactory, odinConfiguration);
            return await worker.Send(odinContext, connection, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendFileOutboxItem(OutboxFileItem fileItem, IOdinContext odinContext,
            DatabaseConnection connection,
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendFileOutboxWorkerAsync>();
            var worker = new SendFileOutboxWorkerAsync(fileItem,
                fileSystemResolver,
                workLogger,
                odinConfiguration,
                odinHttpClientFactory
            );

            return await worker.Send(odinContext, connection, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendPushNotification(OutboxFileItem fileItem, IOdinContext odinContext,
            DatabaseConnection connection,
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendPushNotificationOutboxWorker>();
            var worker = new SendPushNotificationOutboxWorker(fileItem,
                workLogger,
                appRegistrationService,
                pushNotificationService);

            return await worker.Send(odinContext, connection, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendUnencryptedFeedItem(OutboxFileItem fileItem, IOdinContext odinContext,
            DatabaseConnection connection,
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendUnencryptedFeedFileOutboxWorkerAsync>();
            var worker = new SendUnencryptedFeedFileOutboxWorkerAsync(fileItem, fileSystemResolver, workLogger, odinConfiguration, odinHttpClientFactory,
                driveAcl
            );

            return await worker.Send(odinContext, connection, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendDeleteFileRequest(OutboxFileItem fileItem, IOdinContext odinContext,
            DatabaseConnection connection,
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendDeleteFileRequestOutboxWorkerAsync>();
            var worker = new SendDeleteFileRequestOutboxWorkerAsync(fileItem, workLogger, odinConfiguration, odinHttpClientFactory);
            return await worker.Send(odinContext, connection, cancellationToken);
        }
    }
}