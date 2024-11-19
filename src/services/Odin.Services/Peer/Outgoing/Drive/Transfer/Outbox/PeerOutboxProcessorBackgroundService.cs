using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Background.Services;
using Odin.Services.Base;
using Odin.Services.Certificate;
using Odin.Services.Configuration;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Reactions;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox
{
    public class PeerOutboxProcessorBackgroundService(
        ICorrelationContext correlationContext,
        ICertificateCache certificateCache,
        PeerOutbox peerOutbox,
        IOdinHttpClientFactory odinHttpClientFactory,
        OdinConfiguration odinConfiguration,
        ILogger<PeerOutboxProcessorBackgroundService> logger,
        PushNotificationService pushNotificationService,
        IAppRegistrationService appRegistrationService,
        FileSystemResolver fileSystemResolver,
        ILoggerFactory loggerFactory,
        TenantContext tenantContext,
        IDriveAclAuthorizationService driveAcl) : AbstractBackgroundService(logger)
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var domain = tenantContext.HostOdinId.DomainName;

            var tasks = new List<Task>();
            while (!stoppingToken.IsCancellationRequested)
            {
                // Sanity: Make sure we have a certificate for the domain before processing the outbox.
                // Missing certificate can happen in rare, temporary, situations if the certificate has expired
                // or has not yet been created.
                if (certificateCache.LookupCertificate(domain) == null)
                {
                    logger.LogInformation("No certificate found for domain {domain}. Skipping outbox processing", domain);
                    await SleepAsync(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }
                logger.LogDebug("{service} is running", GetType().Name);

                TimeSpan nextRun;
                while (!stoppingToken.IsCancellationRequested && await peerOutbox.GetNextItemAsync() is { } item)
                {
                    var task = ProcessItemThread(item, stoppingToken);
                    tasks.Add(task);
                }

                nextRun = await peerOutbox.NextRunAsync() ?? MaxSleepDuration;

                tasks.RemoveAll(t => t.IsCompleted);

                logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, nextRun);
                await SleepAsync(nextRun, stoppingToken);
            }

            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
        /// </summary>
        private async Task ProcessItemThread(OutboxFileItem fileItem, CancellationToken cancellationToken)
        {
            var originalCorrelationId = correlationContext.Id;
            correlationContext.Id = Guid.NewGuid().ToString();

            var odinContext = new OdinContext
            {
                Tenant = tenantContext.HostOdinId,
                AuthTokenCreated = null,
                Caller = new CallerContext(
                    odinId: (OdinId)"system.domain",
                    masterKey: null,
                    securityLevel: SecurityGroupType.System,
                    circleIds: null,
                    tokenType: ClientTokenType.Other)
            };

            odinContext.SetPermissionContext(new PermissionContext(null, null, true));

            logger.LogDebug("Processing outbox item type: {type} (logref:{originalCorrelationId})", fileItem.Type, originalCorrelationId);

            try
            {
                var (shouldMarkComplete, nextRun) = await ProcessItemUsingWorker(fileItem, odinContext, cancellationToken);
                if (shouldMarkComplete)
                {
                    await peerOutbox.MarkCompleteAsync(fileItem.Marker);

                    await CleanupIfTransientItem(fileItem, odinContext);
                }
                else
                {
                    await RescheduleItem(fileItem, nextRun);
                }
            }
            catch (OperationCanceledException oce)
            {
                await RescheduleItem(fileItem, UnixTimeUtc.Now());

                // Expected when using cancellation token
                logger.LogInformation(oce, "ProcessItem Canceled for file:{file} and recipient: {r} ", fileItem.File, fileItem.Recipient);
            }
            catch (OdinFileReadException fileReadException)
            {
                await peerOutbox.MarkCompleteAsync(fileItem.Marker);

                logger.LogError(fileReadException, "Source file not found {file} item (type: {itemType})", fileItem.File, fileItem.Type);
            }
            catch (OdinOutboxProcessingException e)
            {
                await RescheduleItem(fileItem, UnixTimeUtc.Now());

                logger.LogError(e, "An outbox worker did not handle the outbox processing exception.  Action: Marking Failure" +
                                   "item (type: {itemType}).  File:{file}\t Marker:{marker}",
                    fileItem.Type,
                    fileItem.File,
                    fileItem.Marker);
            }
            catch (Exception e)
            {
                await RescheduleItem(fileItem, UnixTimeUtc.Now());

                logger.LogError(e, "Unhandled exception occured while processing an outbox.  Action: Marking Failure." +
                                   "item (type: {itemType}).  File:{file}\t Marker:{marker}",
                    fileItem.Type,
                    fileItem.File,
                    fileItem.Marker);
            }
        }

        private async Task CleanupIfTransientItem(OutboxFileItem fileItem, IOdinContext odinContext)
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
                            if (!await peerOutbox.HasOutboxFileItemAsync(fileItem))
                            {
                                logger.LogDebug("File was transient and all other outbox records sent; deleting");
                                await fs.Storage.HardDeleteLongTermFile(fileItem.File, odinContext);
                            }
                        });
                }
            }
            catch (Exception e)
            {
                logger.LogInformation(e, "Failed to clean up transient file {file}; record is already marked complete", fileItem.File);
            }
        }

        private async Task RescheduleItem(OutboxFileItem fileItem, UnixTimeUtc nextRun)
        {
            if (fileItem.AttemptCount > odinConfiguration.Host.OutboxOperationMaxAttempts)
            {
                await peerOutbox.MarkCompleteAsync(fileItem.Marker);
                logger.LogInformation(
                    "Outbox: item of type {type} and file {file} failed too many times (attempts: {attempts}) to send.  Action: Marking Complete",
                    fileItem.Type,
                    fileItem.File,
                    fileItem.AttemptCount);
                return;
            }

            await peerOutbox.MarkFailureAsync(fileItem.Marker, nextRun);
            InternalPulseBackgroundProcessor();
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> ProcessItemUsingWorker(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            switch (fileItem.Type)
            {
                case OutboxItemType.PushNotification:
                    return await SendPushNotification(fileItem, odinContext, cancellationToken);

                case OutboxItemType.File:
                    return await SendFileOutboxItem(fileItem, odinContext, cancellationToken);

                case OutboxItemType.RemoteFileUpdate:
                    return await UpdateRemoteFile(fileItem, odinContext, cancellationToken);

                case OutboxItemType.UnencryptedFeedItem:
                    return await SendUnencryptedFeedItem(fileItem, odinContext, cancellationToken);

                case OutboxItemType.DeleteRemoteFile:
                    return await SendDeleteFileRequest(fileItem, odinContext, cancellationToken);

                case OutboxItemType.ReadReceipt:
                    return await SendReadReceipt(fileItem, odinContext, cancellationToken);

                case OutboxItemType.AddRemoteReaction:
                    return await AddRemoteReaction(fileItem, odinContext, cancellationToken);

                case OutboxItemType.DeleteRemoteReaction:
                    return await DeleteRemoteReaction(fileItem, odinContext, cancellationToken);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> AddRemoteReaction(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<AddRemoteReactionOutboxWorker>();
            var worker = new AddRemoteReactionOutboxWorker(fileItem, workLogger, odinHttpClientFactory, odinConfiguration);
            return await worker.Send(odinContext, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> DeleteRemoteReaction(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<DeleteRemoteReactionOutboxWorker>();
            var worker = new DeleteRemoteReactionOutboxWorker(fileItem, workLogger, odinHttpClientFactory, odinConfiguration);
            return await worker.Send(odinContext, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendReadReceipt(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendReadReceiptOutboxWorker>();
            var worker = new SendReadReceiptOutboxWorker(fileItem, workLogger, odinHttpClientFactory, odinConfiguration);
            return await worker.Send(odinContext, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendFileOutboxItem(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendFileOutboxWorkerAsync>();
            var worker = new SendFileOutboxWorkerAsync(fileItem,
                fileSystemResolver,
                workLogger,
                odinConfiguration,
                odinHttpClientFactory
            );

            return await worker.Send(odinContext, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> UpdateRemoteFile(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<UpdateRemoteFileOutboxWorker>();
            var worker = new UpdateRemoteFileOutboxWorker(fileItem,
                fileSystemResolver,
                workLogger,
                odinConfiguration,
                odinHttpClientFactory
            );

            return await worker.Send(odinContext, cancellationToken);
        }
        
        
        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendPushNotification(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendPushNotificationOutboxWorker>();
            var worker = new SendPushNotificationOutboxWorker(fileItem,
                workLogger,
                appRegistrationService,
                pushNotificationService);

            return await worker.Send(odinContext, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendUnencryptedFeedItem(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendUnencryptedFeedFileOutboxWorkerAsync>();
            var worker = new SendUnencryptedFeedFileOutboxWorkerAsync(fileItem, fileSystemResolver, workLogger, odinConfiguration, odinHttpClientFactory,
                driveAcl
            );

            return await worker.Send(odinContext, cancellationToken);
        }

        private async Task<(bool shouldMarkComplete, UnixTimeUtc nextRun)> SendDeleteFileRequest(OutboxFileItem fileItem, IOdinContext odinContext,
            
            CancellationToken cancellationToken)
        {
            var workLogger = loggerFactory.CreateLogger<SendDeleteFileRequestOutboxWorkerAsync>();
            var worker = new SendDeleteFileRequestOutboxWorkerAsync(fileItem, workLogger, odinConfiguration, odinHttpClientFactory);
            return await worker.Send(odinContext, cancellationToken);
        }
    }
}