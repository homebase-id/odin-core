using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Membership.Connections.IcrKeyUpgrade;

#nullable enable

//

public class IcrKeyUpgradeJob(
    IMultiTenantContainerAccessor tenantContainerAccessor,
    ILogger<IcrKeyUpgradeJob> logger) : AbstractJob
{
    public IcrKeyUpgradeJobData Data { get; set; } = new();

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
            var service = scope.Resolve<IcrKeyUpgradeService>();
            await service.Upgrade(Data);
        }
        catch (Exception e)
        {
            logger.LogError(e, "IcrKeyUpgradeJob railed to run");
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
        Data = OdinSystemSerializer.DeserializeOrThrow<IcrKeyUpgradeJobData>(json);
    }

    //
}