using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Time;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.JobManagement;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Drives.Management;

#nullable enable

/// <summary>
/// The owner's bulk removal of drive content. Local only: nothing is sent to peers, and copies they already
/// received stay with them.
/// </summary>
/// <remarks>
/// Both operations return once the change is recorded; a <see cref="DrivePurgeJob"/> removes the files in the
/// background, so a drive of any size neither holds the request nor locks the identity's database for long.
/// </remarks>
public class DriveDeletionService(
    DriveManager driveManager,
    DrivePurgeRegistry drivePurges,
    CircleNetworkService circleNetworkService,
    IAppRegistrationService appRegistrationService,
    IJobManager jobManager,
    IdentityDatabase db,
    TableInboxCached inbox,
    TenantContext tenantContext)
{
    /// <summary>
    /// Hard-deletes every file on an archived, non-system drive and keeps the drive. Pending transfers into and
    /// out of the drive are dropped now; the files present now go in the background, and anything uploaded after
    /// this call stays.
    /// </summary>
    public async Task EmptyDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await AssertDeletableAsync(driveId, odinContext);

        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            await DropPendingTransfersAsync(driveId);
            var now = UnixTimeUtc.Now().milliseconds;
            await drivePurges.SaveAsync(new DrivePurge
            {
                DriveId = driveId,
                TargetDrive = drive.TargetDriveInfo,
                Name = drive.Name,
                AppId = drive.AppId,
                Kind = DrivePurgeKind.Empty,
                Requested = now,
                CreatedAtOrBefore = now
            });
            tx.Commit();
        }

        await SchedulePurgeAsync(driveId, odinContext);
    }

    /// <summary>
    /// Deletes an archived, non-system drive and everything in it: its files, its followers, and every grant
    /// that names it -- circles, connections, YouAuth domains, deposits and apps -- so a drive created later
    /// with the same alias, as an app reinstall does, inherits none of them.
    /// </summary>
    /// <remarks>
    /// The drive, its grants and its pending transfers go now, in one transaction; its files and directories go
    /// in the background. Until they have, its alias cannot be used for a new drive.
    /// </remarks>
    public async Task DeleteDriveAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await AssertDeletableAsync(driveId, odinContext);

        await using (var tx = await db.BeginStackedTransactionAsync())
        {
            await circleNetworkService.RemoveDriveFromAllGrantsAsync(driveId, odinContext);
            await appRegistrationService.RemoveDriveFromAllAppsAsync(driveId, odinContext);
            await DropPendingTransfersAsync(driveId);
            await db.FollowsMeCached.DeleteByDriveAsync(driveId);
            await db.DrivesCached.DeleteAsync(driveId);
            await drivePurges.SaveAsync(new DrivePurge
            {
                DriveId = driveId,
                TargetDrive = drive.TargetDriveInfo,
                Name = drive.Name,
                AppId = drive.AppId,
                Kind = DrivePurgeKind.Delete,
                Requested = UnixTimeUtc.Now().milliseconds
            });
            tx.Commit();
        }

        await SchedulePurgeAsync(driveId, odinContext);
    }

    /// <summary>
    /// Every empty and delete still being purged, with the files left and the last failure, so the owner can see
    /// it happening, know when it is done, and retry one that stopped.
    /// </summary>
    public async Task<List<DrivePurgeStatus>> GetPurgesAsync(IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var statuses = new List<DrivePurgeStatus>();
        foreach (var purge in await drivePurges.GetAllAsync())
        {
            var job = await jobManager.GetJobByHashAsync(DrivePurgeJob.HashFor(odinContext.Tenant, purge.DriveId));
            statuses.Add(new DrivePurgeStatus
            {
                TargetDrive = purge.TargetDrive,
                Name = purge.Name,
                AppId = purge.AppId,
                Kind = purge.Kind,
                Requested = purge.Requested,
                FilesRemaining = await db.MainIndexMetaCached.CountDriveFilesAsync(purge.DriveId,
                    purge.Kind == DrivePurgeKind.Empty ? purge.CreatedAtOrBefore : null),
                LastError = purge.LastError,
                LastErrorAt = purge.LastErrorAt,
                Stopped = job == null
            });
        }

        return statuses;
    }

    /// <summary>Restarts a purge whose job has stopped -- it ran out of retries, or was never queued.</summary>
    public async Task RetryPurgeAsync(Guid driveId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        var purge = await drivePurges.GetAsync(driveId)
                    ?? throw new OdinClientException("This drive is not being emptied or deleted");
        purge.LastError = null;
        purge.LastErrorAt = null;
        await drivePurges.SaveAsync(purge);

        await SchedulePurgeAsync(driveId, odinContext);
    }

    private async Task DropPendingTransfersAsync(Guid driveId)
    {
        await db.Outbox.DeleteByDriveAsync(driveId);
        await inbox.DeleteBoxAsync(driveId);
    }

    private async Task SchedulePurgeAsync(Guid driveId, IOdinContext odinContext)
    {
        var job = jobManager.NewJob<DrivePurgeJob>(tenantContext.DotYouRegistryId);
        job.Data = new DrivePurgeJobData { Tenant = odinContext.Tenant, DriveId = driveId };
        await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = DateTimeOffset.UtcNow,
            MaxAttempts = 20,
            RetryDelay = TimeSpan.FromMinutes(5),
            // Gone at once either way: a job kept under the same hash would swallow the next empty of the drive, or
            // a retry. A failure stays visible on the purge record (LastError) instead.
            OnSuccessDeleteAfter = TimeSpan.Zero,
            OnFailureDeleteAfter = TimeSpan.Zero,
        });
    }

    private async Task<StorageDrive> AssertDeletableAsync(Guid driveId, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (BuiltinDrives.IsProtected(driveId))
        {
            throw new OdinClientException("Cannot delete a system drive or its content");
        }

        // Archiving first is the owner's first "are you sure"; it also stops apps writing to the drive.
        var drive = (await driveManager.GetDriveAsync(driveId, failIfInvalid: true))!;
        if (!drive.IsArchived)
        {
            throw new OdinClientException("Archive the drive before emptying or deleting it");
        }

        return drive;
    }
}

public class DrivePurgeStatus
{
    public TargetDrive TargetDrive { get; init; } = null!;
    public string? Name { get; init; }
    public Guid? AppId { get; init; }
    public DrivePurgeKind Kind { get; init; }

    /// <summary>When the owner asked, in unix ms.</summary>
    public long Requested { get; init; }

    public long FilesRemaining { get; init; }
    public string? LastError { get; init; }
    public long? LastErrorAt { get; init; }

    /// <summary>No job is working on it any more: it ran out of retries. Retry restarts it.</summary>
    public bool Stopped { get; init; }
}
