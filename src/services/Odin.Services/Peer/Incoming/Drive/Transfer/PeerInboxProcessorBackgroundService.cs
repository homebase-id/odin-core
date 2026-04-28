using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.Background.BackgroundServices;
using Odin.Services.Drives.Management;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

// Drains the per-tenant inbox-drive queue. Producers (e.g. query-batch endpoints)
// Enqueue (driveId, IOdinContext) on PeerInboxDriveQueue and then call NotifyWorkAvailableAsync
// to wake this service. We resolve a child DI scope per drive so the work survives
// the originating request returning, and use the caller's cloned context so we have
// the same identity, permissions, and drive grants the original request had.
// ReSharper disable once ClassNeverInstantiated.Global
public class PeerInboxProcessorBackgroundService(
    //
    // DO NOT inject scoped classes that you intend to use in ProcessOneDriveAsync —
    // resolve them from the child scope inside that method instead.
    //
    ILifetimeScope lifetimeScope,
    ICorrelationContext correlationContext,
    PeerInboxDriveQueue driveQueue,
    ILogger<PeerInboxProcessorBackgroundService> logger) : AbstractBackgroundService(logger)
{
    private static string FallbackCorrelationId => Guid.NewGuid().ToString().Remove(9, 4).Insert(9, "INBX");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            while (!stoppingToken.IsCancellationRequested && driveQueue.TryDequeue(out var request))
            {
                await ProcessOneDriveAsync(request, stoppingToken);
            }

            await SleepAsync(MaxSleepDuration, stoppingToken);
        }
    }

    private async Task ProcessOneDriveAsync(PeerInboxDriveQueue.Request request, CancellationToken cancellationToken)
    {
        await using var childScope = lifetimeScope.BeginLifetimeScope($"PeerInboxProcessorBgService:{Guid.NewGuid()}");

        correlationContext.Id = FallbackCorrelationId;

        try
        {
            var driveManager = childScope.Resolve<IDriveManager>();
            var drive = await driveManager.GetDriveAsync(request.DriveId);
            if (drive == null)
            {
                logger.LogDebug("[DeleteFlow] PeerInboxProcessorBgService -> drive {driveId} not found; skipping",
                    request.DriveId);
                return;
            }

            var processor = childScope.Resolve<PeerInboxProcessor>();
            logger.LogDebug("[DeleteFlow] PeerInboxProcessorBgService -> draining inbox for drive {drive} as caller {caller}",
                drive.TargetDriveInfo, request.OdinContext.Caller?.OdinId);
            await processor.ProcessInboxAsync(drive.TargetDriveInfo, request.OdinContext, batchSize: 100);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // shutdown — leave the drive's items in the inbox; next wake will pick them up
        }
        catch (Exception e)
        {
            logger.LogError(e, "[DeleteFlow] PeerInboxProcessorBgService -> error draining drive {driveId}",
                request.DriveId);
        }
    }
}
