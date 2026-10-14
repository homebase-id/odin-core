#nullable enable

using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Logging.Hostname;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Drives.Management;

public class DrivePurgeJobData
{
    public OdinId? Tenant { get; set; }
    public Guid DriveId { get; set; }
}

/// <summary>
/// Removes an emptied or deleted drive's files in the background, a batch per run, so a drive of any size is
/// gone without holding a request -- or the identity's database -- for the length of it.
/// </summary>
/// <remarks>
/// Each batch deletes payloads before rows: a run that fails part-way leaves rows behind and the next run finds
/// those files again, rather than leaving rows whose payloads are already gone. When no files are left, a
/// deleted drive's remaining rows and directories go, and the drive leaves <see cref="DrivePurgeRegistry"/>.
/// What it purges is read from the registry each run, so a delete that follows an empty takes over its job.
/// </remarks>
// ReSharper disable once ClassNeverInstantiated.Global (instantiated by DI via the job type registry)
public class DrivePurgeJob(IMultiTenantContainer tenantContainer, ILogger<DrivePurgeJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("b4e1c7a2-3d58-4f96-8a0b-6e2d9c4f1a37");
    public override string JobType => JobTypeId.ToString();

    /// <summary>Files per run. Internal so a test can force several runs without uploading hundreds.</summary>
    internal static int BatchSize = 500;

    /// <summary>Payload deletes in flight at once: on S3 each is a list plus a delete round trip.</summary>
    private const int PayloadDeleteParallelism = 8;

    public const int MaxAttempts = 20;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromMinutes(5);

    public DrivePurgeJobData Data { get; set; } = new();

    // One purge job per drive: a second empty or delete of the same drive rides the queued one.
    public override string CreateJobHash() => HashFor(Data.Tenant, Data.DriveId);

    /// <summary>The hash a drive's purge job is queued under, to tell whether one still is.</summary>
    public static string HashFor(OdinId? tenant, Guid driveId) =>
        SHA256.HashData((JobTypeId + tenant.ToString() + driveId).ToUtf8ByteArray()).ToBase64();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (Data.Tenant == null || !OdinId.IsValid(Data.Tenant.Value.DomainName))
        {
            logger.LogError("{job} received an empty/invalid tenant; aborting", nameof(DrivePurgeJob));
            return JobExecutionResult.Abort();
        }

        var tenant = Data.Tenant.Value;
        var tenantScope = tenantContainer.LookupTenantScope(tenant);
        if (tenantScope == null)
        {
            logger.LogError("{job} could not resolve tenant scope for {tenant}; aborting", nameof(DrivePurgeJob), tenant);
            return JobExecutionResult.Abort();
        }

        try
        {
            await using var scope = tenantScope.BeginLifetimeScope($"{nameof(DrivePurgeJob)}:Run:{tenant}:{Guid.NewGuid()}");
            scope.Resolve<IStickyHostname>().Hostname = $"{tenant}&";

            var registry = scope.Resolve<DrivePurgeRegistry>();
            var purge = await registry.GetAsync(Data.DriveId);
            if (purge == null)
            {
                return JobExecutionResult.Success();
            }

            var db = scope.Resolve<IdentityDatabase>();
            var paths = scope.Resolve<TenantContext>().TenantPathManager;
            var payloads = scope.Resolve<LongTermPayloadStore>();
            var isDelete = purge.Kind == DrivePurgeKind.Delete;

            var fileIds = await db.MainIndexMetaCached.GetDriveFileIdsAsync(Data.DriveId, BatchSize, purge.Cutoff);

            // A deleted drive's payloads go with its directory at the end; an emptied drive keeps the directory.
            if (!isDelete)
            {
                await Parallel.ForEachAsync(fileIds,
                    new ParallelOptions { MaxDegreeOfParallelism = PayloadDeleteParallelism, CancellationToken = cancellationToken },
                    async (fileId, ct) =>
                        await payloads.DeleteSetAsync(paths.GetPayloadDirectory(Data.DriveId, fileId), fileId, ct));
            }

            await db.MainIndexMetaCached.DeleteFilesAsync(Data.DriveId, fileIds);

            if (purge.LastError != null)
            {
                // Progress again: the owner should no longer see the earlier failure.
                purge.SetError(null);
                await registry.SaveAsync(purge);
            }

            if (fileIds.Count == BatchSize)
            {
                return JobExecutionResult.Repeat(DateTimeOffset.UtcNow);
            }

            if (isDelete)
            {
                // Rows no file index entry points at any more, then the directories.
                await db.MainIndexMetaCached.DeleteDriveContentAsync(Data.DriveId);
                await scope.Resolve<TableInboxCached>().DeleteBoxAsync(Data.DriveId);
                foreach (var (store, directory) in new (IDriveFileStore, string)[]
                         {
                             (payloads, paths.GetDrivePath(Data.DriveId)),
                             (scope.Resolve<UploadFileStore>(), paths.GetDriveUploadPath(Data.DriveId)),
                             (scope.Resolve<InboxFileStore>(), paths.GetDriveInboxPath(Data.DriveId))
                         })
                {
                    TenantPathManager.AssertIsDriveDirectory(directory, Data.DriveId);
                    await store.DeleteDirectoryAsync(directory, cancellationToken);
                }
            }

            // An empty or delete asked for while this run worked has moved the record on; purge again for it.
            var latest = await registry.GetAsync(Data.DriveId);
            if (latest != null && (latest.Kind != purge.Kind || latest.Requested != purge.Requested))
            {
                return JobExecutionResult.Repeat(DateTimeOffset.UtcNow);
            }

            await registry.RemoveAsync(Data.DriveId);
            logger.LogInformation("{job} finished purging drive {drive} for {tenant}", nameof(DrivePurgeJob), Data.DriveId, tenant);
            return JobExecutionResult.Success();
        }
        catch (Exception e)
        {
            logger.LogError(e, "{job} failed purging drive {drive}", nameof(DrivePurgeJob), Data.DriveId);
            await TryRecordErrorAsync(tenantScope, e);
            return JobExecutionResult.Fail();
        }
    }

    /// <summary>Puts the failure where the owner's purge status reads it. Best effort: the run has failed already.</summary>
    private async Task TryRecordErrorAsync(ILifetimeScope tenantScope, Exception error)
    {
        try
        {
            await using var scope = tenantScope.BeginLifetimeScope($"{nameof(DrivePurgeJob)}:Error:{Guid.NewGuid()}");
            var registry = scope.Resolve<DrivePurgeRegistry>();
            var purge = await registry.GetAsync(Data.DriveId);
            if (purge != null)
            {
                purge.SetError(error.Message);
                await registry.SaveAsync(purge);
            }
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "{job} could not record its failure for drive {drive}", nameof(DrivePurgeJob), Data.DriveId);
        }
    }

    public override string SerializeJobData() => OdinSystemSerializer.Serialize(Data);

    public override void DeserializeJobData(string json) =>
        Data = OdinSystemSerializer.DeserializeOrThrow<DrivePurgeJobData>(json);
}
