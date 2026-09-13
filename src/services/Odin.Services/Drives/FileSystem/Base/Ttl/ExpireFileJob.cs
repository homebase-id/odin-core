#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Logging.Hostname;
using Odin.Core.Serialization;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Drives.FileSystem.Base.Ttl;

/// <summary>
/// Soft deletes a file once its <see cref="FileMetadata.Ttl"/> has come due, then schedules the
/// tombstone reap.
///
/// The job is deliberately idempotent and re-reads the header rather than trusting the schedule: a
/// file's TTL can be shortened after this job was queued, and a negative TTL resolves to a real time
/// only on first read. So more than one of these may be queued for the same file, and any that finds
/// nothing left to do simply succeeds.
///
/// It soft deletes rather than hard deletes. A hard delete drops the row, so a client polling
/// query-modified never learns the file is gone and goes on showing a stale copy — which is exactly
/// what retention is supposed to prevent. The tombstone is reaped later by <see cref="ReapFileJob"/>.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global (instantiated by DI via the job type registry)
public class ExpireFileJob(
    IMultiTenantContainer tenantContainer,
    ILogger<ExpireFileJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("7d4c9a10-1f63-4a52-9b8e-3c05d7e1af26");
    public override string JobType => JobTypeId.ToString();

    public FileTtlJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (Data.Tenant == null || !OdinId.IsValid(Data.Tenant.Value.DomainName))
        {
            logger.LogError("{job} received an empty/invalid tenant; aborting", nameof(ExpireFileJob));
            return JobExecutionResult.Abort();
        }

        var tenant = Data.Tenant.Value;
        var tenantScope = tenantContainer.LookupTenantScope(tenant);
        if (tenantScope == null)
        {
            logger.LogError("{job} could not resolve tenant scope for {tenant}; aborting", nameof(ExpireFileJob), tenant);
            return JobExecutionResult.Abort();
        }

        try
        {
            await using var scope = tenantScope.BeginLifetimeScope(
                $"{nameof(ExpireFileJob)}:Run:{tenant}:{Guid.NewGuid()}");

            scope.Resolve<IStickyHostname>().Hostname = $"{tenant}&";

            var drive = await scope.Resolve<IDriveManager>().GetDriveAsync(Data.DriveId);
            var odinContext = FileTtlJobContext.BuildSystemContext(tenant, drive.TargetDriveInfo);
            var fs = scope.Resolve<FileSystemResolver>().ResolveFileSystem(Data.FileSystemType);
            var file = new InternalDriveFileId { DriveId = Data.DriveId, FileId = Data.FileId };

            var header = await fs.Storage.GetServerFileHeaderForExpiry(file, odinContext);
            if (header == null || header.FileMetadata.FileState != FileState.Active)
            {
                // Already gone, or already a tombstone. Another queued copy of this job got here first.
                return JobExecutionResult.Success();
            }

            var ttl = header.FileMetadata.Ttl;
            if (FileTtl.IsNever(ttl))
            {
                // The TTL was cleared on a file that never expires now. Nothing to do.
                return JobExecutionResult.Success();
            }

            var now = UnixTimeUtc.Now();
            var dueAt = FileTtl.ExpiresAt(ttl, header.FileMetadata.Created);
            if (dueAt != null && dueAt.Value > now.milliseconds)
            {
                // Not due yet — the TTL moved out from under us, or the runner fired early. Come back
                // when it is actually due rather than deleting a live file.
                logger.LogDebug("{job} for {file} is not due until {dueAt}; deferring", nameof(ExpireFileJob), file, dueAt);
                return JobExecutionResult.Defer(DateTimeOffset.FromUnixTimeMilliseconds(dueAt.Value));
            }

            await fs.Storage.SoftDeleteLongTermFile(file, odinContext, null);

            logger.LogInformation("{job} soft deleted expired file {file} on drive {drive} for {tenant}",
                nameof(ExpireFileJob), Data.FileId, Data.DriveId, tenant);

            await scope.Resolve<FileExpiryScheduler>().ScheduleReapAsync(file, Data.FileSystemType, tenant);
        }
        catch (Exception e)
        {
            logger.LogError(e, "{job} failed for file {file} on drive {drive}", nameof(ExpireFileJob), Data.FileId, Data.DriveId);
            return JobExecutionResult.Fail();
        }

        return JobExecutionResult.Success();
    }

    public override string SerializeJobData() => OdinSystemSerializer.Serialize(Data);

    public override void DeserializeJobData(string json) =>
        Data = OdinSystemSerializer.DeserializeOrThrow<FileTtlJobData>(json);
}
