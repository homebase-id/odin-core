using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.JobManagement;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Notifications;

namespace Odin.Services.Tenant.BackgroundService.Services;

public class OutboxBackgroundService(
    IPeerOutbox peerOutbox,
    IOdinHttpClientFactory odinHttpClientFactory,
    OdinConfiguration odinConfiguration,
    ILogger<OutboxBackgroundService> logger,
    PushNotificationService pushNotificationService,
    IAppRegistrationService appRegistrationService,
    FileSystemResolver fileSystemResolver,
    IJobManager jobManager,
    ILoggerFactory loggerFactory,
    TenantSystemStorage tenantSystemStorage,
    Tenant tenant)
    : AbstractTenantBackgroundService(tenant)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var sleepDuration = TimeSpan.FromSeconds(1);

        var tasks = new List<Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            using (var cn = tenantSystemStorage.CreateConnection())
            {
                while (!stoppingToken.IsCancellationRequested && await peerOutbox.GetNextItem(cn) is { } item)
                {
                    var task = ProcessItem(item, OdinContext, stoppingToken);
                    tasks.Add(task);
                }
            }

            tasks.RemoveAll(t => t.IsCompleted);

            await SleepAsync(sleepDuration, stoppingToken);
        }
        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
    /// </summary>
    private async Task ProcessItem(OutboxFileItem fileItem, IOdinContext odinContext, CancellationToken cancellationToken)
    {
        //TODO: add benchmark
        logger.LogDebug("Processing outbox item type: {type}", fileItem.Type);

        try
        {
            switch (fileItem.Type)
            {
                case OutboxItemType.PushNotification:
                    await SendPushNotification(fileItem, odinContext, cancellationToken);
                    break;

                case OutboxItemType.File:
                    await SendFileOutboxItem(fileItem, odinContext, cancellationToken);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when using cancellation token
        }
        catch (Exception e)
        {
            logger.LogError(e, "Unhandled exception occured while processing an outbox " +
                               "item.  File:{file}\t Marker:{marker}", fileItem.File, fileItem.Marker);
        }
    }

    private async Task SendFileOutboxItem(OutboxFileItem fileItem, IOdinContext odinContext, CancellationToken cancellationToken)
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
        using var connection = tenantSystemStorage.CreateConnection();
        await worker.Send(odinContext, connection, cancellationToken);
    }

    private async Task SendPushNotification(OutboxFileItem fileItem, IOdinContext odinContext, CancellationToken cancellationToken)
    {
        var worker = new SendPushNotificationOutboxWorker(fileItem,
            appRegistrationService,
            pushNotificationService,
            peerOutbox);

        using var connection = tenantSystemStorage.CreateConnection();
        await worker.Send(odinContext, connection, cancellationToken);
    }

}