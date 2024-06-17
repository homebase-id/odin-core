using System;
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

public class OutboxBackgroundService : AbstractTenantBackgroundService
{
    private readonly IPeerOutbox _peerOutbox;
    private readonly IOdinHttpClientFactory _odinHttpClientFactory;
    private readonly OdinConfiguration _odinConfiguration;
    private readonly ILogger<OutboxBackgroundService> _logger;
    private readonly PushNotificationService _pushNotificationService;
    private readonly IAppRegistrationService _appRegistrationService;
    private readonly FileSystemResolver _fileSystemResolver;
    private readonly IJobManager _jobManager;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TenantSystemStorage _tenantSystemStorage;
    private readonly Tenant _tenant;

    public OutboxBackgroundService(
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
    {
        _peerOutbox = peerOutbox;
        _odinHttpClientFactory = odinHttpClientFactory;
        _odinConfiguration = odinConfiguration;
        _logger = logger;
        _pushNotificationService = pushNotificationService;
        _appRegistrationService = appRegistrationService;
        _fileSystemResolver = fileSystemResolver;
        _jobManager = jobManager;
        _loggerFactory = loggerFactory;
        _tenantSystemStorage = tenantSystemStorage;
        _tenant = tenant;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("XXXXXXXXXXXXXXXXXXXXXXXXXXXX ");
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogWarning("XXXXXXXXXXXXXXXXXXXXXXXXXXXX {IsCancellationRequested}",
            stoppingToken.IsCancellationRequested);
    }

    private async Task ProcessItems(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogWarning("XXXXXXXXXXXXXXXXXXXXXXXXXXXX ");
            await Task.Delay(1000, stoppingToken);
        }

        _logger.LogWarning("XXXXXXXXXXXXXXXXXXXXXXXXXXXX {IsCancellationRequested}",
            stoppingToken.IsCancellationRequested);
    }

    // // TODO: initialize odinContext
        // var odinContext = new OdinContext();
        //
        // var tasks = new List<Task>();
        // try
        // {
        //     while (!stoppingToken.IsCancellationRequested)
        //     {
        //         using (var cn = tenantSystemStorage.CreateConnection())
        //         {
        //             while (!stoppingToken.IsCancellationRequested && await peerOutbox.GetNextItem(cn) is { } item)
        //             {
        //                 var task = ProcessItem(item, odinContext, stoppingToken);
        //                 tasks.Add(task);
        //             }
        //         }
        //
        //         tasks.RemoveAll(t => t.IsCompleted);
        //
        //         if (!stoppingToken.IsCancellationRequested)
        //         {
        //             await Task.Delay(1000, stoppingToken);
        //         }
        //     }
        // }
        // finally
        // {
        //     await Task.WhenAll(tasks); // this assumes that all tasks can be cancelled with 'stoppingToken'
        // }

    // }

    //

    /// <summary>
    /// Processes the item according to its type.  When finished, it will update the outbox based on success or failure
    /// </summary>
    ///
    private async Task ProcessItem(OutboxFileItem fileItem, IOdinContext odinContext, CancellationToken stoppingToken)
    {
        try
        {
            //TODO: add benchmark
            _logger.LogDebug("Processing outbox item type: {type}", fileItem.Type);

            switch (fileItem.Type)
            {
                case OutboxItemType.PushNotification:
                    await SendPushNotification(fileItem, odinContext, stoppingToken);
                    break;

                case OutboxItemType.File:
                    await SendFileOutboxItem(fileItem, odinContext, stoppingToken);
                    break;

                // case OutboxItemType.Reaction:
                //     return await SendReactionItem(item, odinContext);

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Outbox processing cancelled for item type {type}", fileItem.Type);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Error processing outbox item type {type}", fileItem.Type);
        }

    }

    private async Task SendFileOutboxItem(
        OutboxFileItem fileItem,
        IOdinContext odinContext,
        CancellationToken stoppingToken)
    {
        var workLogger = _loggerFactory.CreateLogger<SendFileOutboxWorkerAsync>();

        var worker = new SendFileOutboxWorkerAsync(fileItem,
            _fileSystemResolver,
            workLogger,
            _peerOutbox,
            _odinConfiguration,
            _odinHttpClientFactory,
            _jobManager
        );

        using var cn = _tenantSystemStorage.CreateConnection();
        await worker.Send(odinContext, cn, stoppingToken);
    }

    private async Task SendPushNotification(
        OutboxFileItem fileItem,
        IOdinContext odinContext,
        CancellationToken stoppingToken)
    {
        var worker = new SendPushNotificationOutboxWorker(fileItem,
            _appRegistrationService,
            _pushNotificationService,
            _peerOutbox);

        using var cn = _tenantSystemStorage.CreateConnection();
        await worker.Send(odinContext, cn, stoppingToken);
    }

}