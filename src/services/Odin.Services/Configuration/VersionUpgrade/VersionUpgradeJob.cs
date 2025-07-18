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
using Odin.Services.JobManagement;
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Configuration.VersionUpgrade;

#nullable enable

//

public class VersionUpgradeJob(
    IMultiTenantContainer tenantContainer,
    ILogger<VersionUpgradeJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("0607585d-6adc-4993-a6e4-11638c7071d6");
    public override string JobType => JobTypeId.ToString();

    public VersionUpgradeJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        try
        {
            if (!OdinId.IsValid(Data.Tenant))
            {
                logger.LogError("Version Upgrade Job received empty tenant; aborting");
                return JobExecutionResult.Abort();
            }

            // Create a new lifetime scope for the tenant so db connections are isolated
            await using var scope = tenantContainer.GetTenantScope(Data.Tenant!)
                .BeginLifetimeScope($"VersionUpgradeJob:Run:{Data.Tenant}:{Guid.NewGuid()}");

            var stickyHostnameContext = scope.Resolve<IStickyHostname>();
            stickyHostnameContext.Hostname = $"{Data.Tenant}&";
            
            var versionUpgradeService = scope.Resolve<VersionUpgradeService>();
            await versionUpgradeService.UpgradeAsync(Data, cancellationToken);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Version Upgrade Job railed to run");
            return JobExecutionResult.Fail();
        }

        // do the thing
        return JobExecutionResult.Success();
    }

    public override string? CreateJobHash()
    {
        var text = JobType + Data.Tenant;
        return SHA256.HashData(text.ToUtf8ByteArray()).ToBase64();
    }

    //

    public override string SerializeJobData()
    {
        return OdinSystemSerializer.Serialize(Data);
    }

    //

    public override void DeserializeJobData(string json)
    {
        Data = OdinSystemSerializer.DeserializeOrThrow<VersionUpgradeJobData>(json);
    }

    //
}