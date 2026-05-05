using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.LastSeen;
using Directory = System.IO.Directory;

namespace Odin.Services.Background.BackgroundServices.Tenant;

// Detection-only sweep over the per-tenant peer inbox staging dirs.
// Logs files older than OrphanAgeThreshold without deleting; the goal is to
// surface leaks (e.g. CleanupInboxFiles bugs) before we trust the cleanup path.
//
// Orphan = file older than OrphanAgeThreshold AND tenant has been seen at
// some point at least OrphanAgeThreshold after the file was written. The
// per-file last-seen check avoids false-positives for files that piled up
// while the tenant was offline and simply haven't been processed yet.
public sealed class InboxOrphanScanBackgroundService(
    ILogger<InboxOrphanScanBackgroundService> logger,
    ILastSeenService lastSeenService,
    TenantContext tenantContext)
    : AbstractBackgroundService(logger)
{
    private static readonly TimeSpan OrphanAgeThreshold = TimeSpan.FromHours(24);
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger initial run across tenants to avoid simultaneous IO storms.
        // await SleepAsync(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(120), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);

            try
            {
                await InboxOrphanScan.ExecuteAsync(
                    logger,
                    tenantContext.TenantPathManager.InboxDrivesPath,
                    OrphanAgeThreshold,
                    lastSeenService,
                    tenantContext.HostOdinId,
                    stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "InboxOrphanScanBackgroundService: {message}", e.Message);
            }

            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, ScanInterval);
            await SleepAsync(ScanInterval, stoppingToken);
        }
    }
}

//

public static class InboxOrphanScan
{
    public static async Task ExecuteAsync(
        ILogger logger,
        string inboxDrivesPath,
        TimeSpan ageThreshold,
        ILastSeenService lastSeenService,
        OdinId hostOdinId,
        CancellationToken stoppingToken = default)
    {
        if (!Directory.Exists(inboxDrivesPath))
        {
            return;
        }

        // If the tenant has never been seen, no file can satisfy the orphan
        // condition (tenant-seen-since-file-write+threshold) — skip entirely.
        var lastSeenUnix = await lastSeenService.GetLastSeenAsync(hostOdinId);
        if (lastSeenUnix == null)
        {
            logger.LogDebug(
                "{service} skipping {tenant}: lastSeen=never",
                nameof(InboxOrphanScan),
                hostOdinId);
            return;
        }

        var lastSeen = lastSeenUnix.Value.ToDateTime();
        var cutoffTime = DateTime.UtcNow.Subtract(ageThreshold);

        var driveFolders = Directory.GetDirectories(inboxDrivesPath);
        var totalOrphans = 0;

        foreach (var driveFolder in driveFolders)
        {
            stoppingToken.ThrowIfCancellationRequested();
            totalOrphans += ScanDriveInbox(logger, driveFolder, cutoffTime, lastSeen, ageThreshold, stoppingToken);
        }

        if (totalOrphans > 0)
        {
            logger.LogError(
                "Inbox orphan summary: {count} stale file(s) under {root} (threshold: {threshold}, tenant lastSeen: {lastSeen:o})",
                totalOrphans,
                inboxDrivesPath,
                ageThreshold,
                lastSeen);
        }
    }

    //

    private static int ScanDriveInbox(
        ILogger logger,
        string driveFolder,
        DateTime cutoffTime,
        DateTime tenantLastSeen,
        TimeSpan ageThreshold,
        CancellationToken stoppingToken)
    {
        if (!Directory.Exists(driveFolder))
        {
            return 0;
        }

        logger.LogDebug("{service} is scanning {folder}", nameof(InboxOrphanScan), driveFolder);

        var orphanCount = 0;

        try
        {
            var files = Directory.GetFiles(driveFolder);

            foreach (var file in files)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    var fileInfo = new FileInfo(file);
                    var fileWrite = fileInfo.LastWriteTimeUtc;

                    // Orphan only if file is older than threshold AND tenant has been
                    // seen at least one threshold-period after the file was written
                    // (i.e., they had a chance to process it but didn't).
                    if (fileWrite < cutoffTime && tenantLastSeen > fileWrite + ageThreshold)
                    {
                        orphanCount++;
                        logger.LogError(
                            "Inbox orphan: {file} lastWrite={lastWrite:o} age={age} tenantLastSeen={lastSeen:o}",
                            file,
                            fileWrite,
                            DateTime.UtcNow - fileWrite,
                            tenantLastSeen);
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "InboxOrphanScan({file}): {message}", file, e.Message);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "InboxOrphanScan({folder}): {message}", driveFolder, e.Message);
        }

        return orphanCount;
    }
}
