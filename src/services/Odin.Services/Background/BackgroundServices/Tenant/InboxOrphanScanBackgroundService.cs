using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Directory = System.IO.Directory;

namespace Odin.Services.Background.BackgroundServices.Tenant;

// Detection-only sweep over the per-tenant peer inbox staging dirs. Logs files older than
// OrphanAgeThreshold without deleting; the goal is to surface leaks (e.g. CleanupInboxFiles bugs)
// before we trust the cleanup path.
//
// What this scanner considers an "orphan":
//
//   An inbox-staged file on disk represents one of two states:
//     (1) Work in progress — there is still a row in the Inbox table keyed by the file's FileId.
//         The peer transfer hasn't been processed yet (or is mid-process). Legitimate, leave alone.
//     (2) Leftover — the Inbox row is gone (PopCommit/MarkCompleteAsync ran) but the on-disk
//         staging files weren't cleaned up. That's the bug-class we want to catch.
//
//   The check:
//     - For each drive folder, fetch the set of fileIds the Inbox table currently holds for that
//       drive in one SELECT (TransitInboxBoxStorage.GetPendingFileIdsAsync).
//     - For each on-disk file, parse its FileId out of the filename. If the FileId is NOT in the
//       pending set AND the file is older than OrphanAgeThreshold, log it as an orphan.
//
//   The age gate exists because the multipart receive path (PeerDriveIncomingTransferService)
//   writes staging files to disk BEFORE RouteToInboxAsync inserts the row. A narrow race
//   window means a brand-new upload can look like an orphan; 24h is comfortably past that.
//
// Why this used to consult ILastSeenService and no longer does:
//
//   The previous heuristic was "file is older than threshold AND tenant lastSeen is more recent
//   than fileWrite + threshold". That relied on LastSeenMiddleware faithfully recording when the
//   tenant's *owner* was engaged.
//
//   In practice LastSeenService is a system-singleton keyed by domain, and the middleware bumps
//   lastSeen[X] whenever identity X appears as the Caller on any request hitting this physical
//   box — including peer requests issued by the tenant's own background jobs (e.g.
//   SecurityHealthCheckJob's shamir-recovery shard verifications). Those land on other local
//   tenants' capi endpoints with Caller=X, which legitimately bumps lastSeen[X] even though the
//   human owner of X is fully offline and the inbox processor never ran. Net effect: lastSeen
//   advances roughly monthly from background plumbing alone, the orphan-eligibility window
//   opens, and pending inbox items get mis-flagged as orphans.
//
//   Querying the Inbox table directly is precise and immune to that pollution. Doing the
//   query at drive granularity (set of fileIds, not one query per file) keeps it cheap on
//   busy drives where a per-file lookup loop would be wasteful.
public sealed class InboxOrphanScanBackgroundService(
    ILogger<InboxOrphanScanBackgroundService> logger,
    TransitInboxBoxStorage transitInboxBoxStorage,
    TenantContext tenantContext)
    : AbstractBackgroundService(logger)
{
    private static readonly TimeSpan OrphanAgeThreshold = TimeSpan.FromHours(24);
    private static readonly TimeSpan ScanInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Stagger initial run across tenants to avoid simultaneous IO storms.
        await SleepAsync(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(120), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);

            try
            {
                await InboxOrphanScan.ExecuteAsync(
                    logger,
                    tenantContext.TenantPathManager.InboxDrivesPath,
                    OrphanAgeThreshold,
                    transitInboxBoxStorage.GetPendingFileIdsAsync,
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
    // getPendingFileIdsAsync(driveId): the scanner's only external dependency. Returns the set
    // of fileIds the Inbox table holds for that drive in any state (queued, popped, mid-process).
    // Injected as a delegate so this class can be unit-tested without a DB.
    public static async Task ExecuteAsync(
        ILogger logger,
        string inboxDrivesPath,
        TimeSpan ageThreshold,
        Func<Guid, Task<HashSet<Guid>>> getPendingFileIdsAsync,
        CancellationToken stoppingToken = default)
    {
        if (!Directory.Exists(inboxDrivesPath))
        {
            logger.LogDebug(
                "{service}: inbox drives path {path} does not exist — nothing to scan",
                nameof(InboxOrphanScan), inboxDrivesPath);
            return;
        }

        var cutoffTime = DateTime.UtcNow.Subtract(ageThreshold);

        var driveFolders = Directory.GetDirectories(inboxDrivesPath);
        logger.LogDebug(
            "{service}: starting sweep of {folderCount} drive folder(s) under {root} (threshold: {threshold}, cutoff: {cutoff:o})",
            nameof(InboxOrphanScan), driveFolders.Length, inboxDrivesPath, ageThreshold, cutoffTime);

        var totalOrphans = 0;

        foreach (var driveFolder in driveFolders)
        {
            stoppingToken.ThrowIfCancellationRequested();
            totalOrphans += await ScanDriveInboxAsync(
                logger, driveFolder, cutoffTime, getPendingFileIdsAsync, stoppingToken);
        }

        if (totalOrphans > 0)
        {
            logger.LogError(
                "Inbox orphan sweep summary: {count} stale file(s) under {root} (threshold: {threshold})",
                totalOrphans,
                inboxDrivesPath,
                ageThreshold);
        }
        else
        {
            logger.LogDebug(
                "{service}: sweep complete — no orphans found across {folderCount} drive folder(s) under {root}",
                nameof(InboxOrphanScan), driveFolders.Length, inboxDrivesPath);
        }
    }

    //

    private static async Task<int> ScanDriveInboxAsync(
        ILogger logger,
        string driveFolder,
        DateTime cutoffTime,
        Func<Guid, Task<HashSet<Guid>>> getPendingFileIdsAsync,
        CancellationToken stoppingToken)
    {
        if (!Directory.Exists(driveFolder))
        {
            return 0;
        }

        if (!TryParseDriveId(driveFolder, out var driveId))
        {
            // Unrecognized folder name — not a drive we know how to query. Leave alone.
            logger.LogDebug(
                "{service}: skipping non-drive folder {folder}", nameof(InboxOrphanScan), driveFolder);
            return 0;
        }

        logger.LogDebug("{service} is scanning {folder}", nameof(InboxOrphanScan), driveFolder);

        HashSet<Guid> pendingFileIds;
        try
        {
            pendingFileIds = await getPendingFileIdsAsync(driveId);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Conservative: if we can't tell which fileIds are pending, don't flag anything.
            // False negative beats a false positive the operator has to chase down.
            logger.LogError(e, "InboxOrphanScan(driveId={driveId}): {message}", driveId, e.Message);
            return 0;
        }

        var orphanCount = 0;
        var totalFiles = 0;
        var skippedUnparseable = 0;
        var skippedTooFresh = 0;
        var skippedStillPending = 0;

        try
        {
            var files = Directory.GetFiles(driveFolder);
            totalFiles = files.Length;
            foreach (var file in files)
            {
                stoppingToken.ThrowIfCancellationRequested();

                if (!TryParseFileId(file, out var fileId))
                {
                    // Unrecognized filename — not something we know how to map to an inbox row.
                    skippedUnparseable++;
                    continue;
                }

                var fileInfo = new FileInfo(file);
                if (fileInfo.LastWriteTimeUtc >= cutoffTime)
                {
                    // Too fresh — could legitimately be mid-upload (the multipart receive writes
                    // staging files before RouteToInboxAsync inserts the row).
                    skippedTooFresh++;
                    continue;
                }

                if (pendingFileIds.Contains(fileId))
                {
                    // Inbox table still has a row for this fileId — legitimately pending.
                    skippedStillPending++;
                    continue;
                }

                orphanCount++;
                logger.LogDebug(
                    "Inbox orphan: {file} driveId={driveId} fileId={fileId} lastWrite={lastWrite:o} age={age}",
                    fileInfo.FullName,
                    driveId,
                    fileId,
                    fileInfo.LastWriteTimeUtc,
                    DateTime.UtcNow - fileInfo.LastWriteTimeUtc);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "InboxOrphanScan({folder}): {message}", driveFolder, e.Message);
        }

        logger.LogDebug(
            "{service}: drive {driveId} scanned — files={totalFiles} pendingRows={pendingRows} " +
            "skipped(unparseable={unparseable}, tooFresh={tooFresh}, stillPending={stillPending}) orphans={orphans}",
            nameof(InboxOrphanScan),
            driveId,
            totalFiles,
            pendingFileIds.Count,
            skippedUnparseable,
            skippedTooFresh,
            skippedStillPending,
            orphanCount);

        return orphanCount;
    }

    //

    // Drive inbox folders are named by TenantPathManager.GuidToPathSafeString(driveId), i.e.
    // the 32-char lowercase hex form ("N" format) of the drive's GUID.
    private static bool TryParseDriveId(string driveFolder, out Guid driveId)
    {
        var name = new DirectoryInfo(driveFolder).Name;
        return Guid.TryParseExact(name, "N", out driveId);
    }

    // Inbox staging filenames are produced by TenantPathManager.GetFilename, which formats as
    // "{fileId:N}.{extension}" — a 32-char lowercase hex GUID followed by a '.' followed by the
    // extension (which may itself contain dots and dashes, e.g. "convo_img-...-1080x810.thumb").
    // The FileId is therefore exactly the 32 chars before the first '.'.
    private static bool TryParseFileId(string filePath, out Guid fileId)
    {
        var name = Path.GetFileName(filePath);
        if (name.Length < 33 || name[32] != '.')
        {
            fileId = Guid.Empty;
            return false;
        }

        return Guid.TryParseExact(name.AsSpan(0, 32), "N", out fileId);
    }
}
