#nullable enable

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.JobManagement;

namespace Odin.Services.Drives.FileSystem.Base.Ttl;

/// <summary>
/// Queues the deletion of a file that carries a <see cref="FileMetadata.Ttl"/>.
///
/// Scheduling happens at <c>CommitNewFile</c>, the one call every write converges on — local upload,
/// comment writer, update writer, peer receive and profile writes all land there. Because a
/// peer-received file arrives through the same call, each identity ends up scheduling the deletion of
/// its own copy with no retention message and no fan-out; that is what makes group retention work
/// across independent servers.
///
/// Jobs are queued, never tracked or cancelled. A file may end up with several queued jobs — its TTL
/// was shortened, or a negative TTL resolved on first read — and <see cref="ExpireFileJob"/> re-reads
/// the header each run, so a redundant one is a no-op. That is far simpler than trying to keep one
/// job id per file in sync with the header.
/// </summary>
public class FileExpiryScheduler(
    IJobManager jobManager,
    TenantContext tenantContext,
    ILogger<FileExpiryScheduler> logger)
{
    /// <summary>
    /// Schedules the soft delete for <paramref name="header"/>, if it expires at all. A negative
    /// (expire-after-first-read) TTL is scheduled at its unread backstop, so a file nobody ever opens
    /// still goes away; when it is read, the resolved time is scheduled on top.
    /// </summary>
    public async Task ScheduleExpiryAsync(ServerFileHeader header, IOdinContext odinContext)
    {
        var metadata = header.FileMetadata;
        var dueAt = FileTtl.ExpiresAt(metadata.Ttl, metadata.Created);
        if (dueAt == null)
        {
            return;
        }

        await ScheduleExpiryAtAsync(
            metadata.File,
            header.ServerMetadata.FileSystemType,
            dueAt.Value,
            odinContext.Tenant);
    }

    public async Task ScheduleExpiryAtAsync(InternalDriveFileId file, FileSystemType fileSystemType, long dueAtMs, OdinId tenant)
    {
        var job = jobManager.NewJob<ExpireFileJob>(tenantContext.DotYouRegistryId);
        job.Data = new FileTtlJobData
        {
            Tenant = tenant,
            DriveId = file.DriveId,
            FileId = file.FileId,
            FileSystemType = fileSystemType
        };

        // A TTL already in the past runs immediately rather than never.
        var runAt = DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(dueAtMs, UnixTimeUtc.Now().milliseconds));

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = runAt,
            MaxAttempts = 3,
            RetryDelay = TimeSpan.FromMinutes(5),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
            OnFailureDeleteAfter = TimeSpan.FromDays(1),
        });

        logger.LogDebug("Scheduled expiry job {jobId} for file {file} on drive {drive} at {runAt}",
            jobId, file.FileId, file.DriveId, runAt);
    }

    /// <summary>
    /// Schedules the tombstone reap that follows a soft delete, after <see cref="FileTtl.TombstoneGrace"/>.
    /// </summary>
    public async Task ScheduleReapAsync(InternalDriveFileId file, FileSystemType fileSystemType, OdinId tenant)
    {
        var job = jobManager.NewJob<ReapFileJob>(tenantContext.DotYouRegistryId);
        job.Data = new FileTtlJobData
        {
            Tenant = tenant,
            DriveId = file.DriveId,
            FileId = file.FileId,
            FileSystemType = fileSystemType
        };

        var runAt = DateTimeOffset.UtcNow + FileTtl.TombstoneGrace;

        var jobId = await jobManager.ScheduleJobAsync(job, new JobSchedule
        {
            RunAt = runAt,
            MaxAttempts = 3,
            RetryDelay = TimeSpan.FromHours(1),
            OnSuccessDeleteAfter = TimeSpan.FromMinutes(1),
            OnFailureDeleteAfter = TimeSpan.FromDays(1),
        });

        logger.LogDebug("Scheduled reap job {jobId} for tombstone {file} on drive {drive} at {runAt}",
            jobId, file.FileId, file.DriveId, runAt);
    }
}
