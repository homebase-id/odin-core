#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Logging.Hostname;
using Odin.Core.Serialization;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.Management;
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Drives.FileSystem.Base.Ttl;

/// <summary>
/// Hard deletes a tombstone left behind by <see cref="ExpireFileJob"/>, once
/// <see cref="FileTtl.TombstoneGrace"/> has passed and every client has certainly synced the deletion.
///
/// Without this the index only ever grows: a soft delete keeps its row forever, so 90-day retention on
/// a busy group would leave one permanent row per message.
/// </summary>
// ReSharper disable once ClassNeverInstantiated.Global (instantiated by DI via the job type registry)
public class ReapFileJob(
    IMultiTenantContainer tenantContainer,
    ILogger<ReapFileJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("2b81f0c7-59ad-4e3f-8a16-d0e94c7b5312");
    public override string JobType => JobTypeId.ToString();

    public FileTtlJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        if (Data.Tenant == null || !OdinId.IsValid(Data.Tenant.Value.DomainName))
        {
            logger.LogError("{job} received an empty/invalid tenant; aborting", nameof(ReapFileJob));
            return JobExecutionResult.Abort();
        }

        var tenant = Data.Tenant.Value;
        var tenantScope = tenantContainer.LookupTenantScope(tenant);
        if (tenantScope == null)
        {
            logger.LogError("{job} could not resolve tenant scope for {tenant}; aborting", nameof(ReapFileJob), tenant);
            return JobExecutionResult.Abort();
        }

        try
        {
            await using var scope = tenantScope.BeginLifetimeScope(
                $"{nameof(ReapFileJob)}:Run:{tenant}:{Guid.NewGuid()}");

            scope.Resolve<IStickyHostname>().Hostname = $"{tenant}&";

            var drive = await scope.Resolve<IDriveManager>().GetDriveAsync(Data.DriveId);
            var odinContext = FileTtlJobContext.BuildSystemContext(tenant, drive.TargetDriveInfo);
            var fs = scope.Resolve<FileSystemResolver>().ResolveFileSystem(Data.FileSystemType);
            var file = new InternalDriveFileId { DriveId = Data.DriveId, FileId = Data.FileId };

            var header = await fs.Storage.GetServerFileHeaderForExpiry(file, odinContext);
            if (header == null)
            {
                return JobExecutionResult.Success();
            }

            if (header.FileMetadata.FileState == FileState.Active)
            {
                // The fileId was reused by a live file. Never reap an active file.
                logger.LogWarning("{job} found file {file} active; refusing to hard delete", nameof(ReapFileJob), file);
                return JobExecutionResult.Success();
            }

            await fs.Storage.HardDeleteLongTermFile(file, odinContext);

            logger.LogInformation("{job} reaped tombstone {file} on drive {drive} for {tenant}",
                nameof(ReapFileJob), Data.FileId, Data.DriveId, tenant);
        }
        catch (Exception e)
        {
            logger.LogError(e, "{job} failed for file {file} on drive {drive}", nameof(ReapFileJob), Data.FileId, Data.DriveId);
            return JobExecutionResult.Fail();
        }

        return JobExecutionResult.Success();
    }

    public override string SerializeJobData() => OdinSystemSerializer.Serialize(Data);

    public override void DeserializeJobData(string json) =>
        Data = OdinSystemSerializer.DeserializeOrThrow<FileTtlJobData>(json);
}
