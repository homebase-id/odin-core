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
using Odin.Services.Tenant.Container;

namespace Odin.Services.Configuration.VersionUpgrade;

#nullable enable

//

public class VersionUpgradeJob(
    IMultiTenantContainerAccessor tenantContainerAccessor,
    ILogger<VersionUpgradeJob> logger) : AbstractJob
{
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

            var scope = tenantContainerAccessor.Container().GetTenantScope(Data.Tenant!);
            
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