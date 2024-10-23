using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Logging.Hostname;
using Odin.Core.Serialization;
using Odin.Services.JobManagement;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Membership.Connections.IcrKeyAvailableWorker;

#nullable enable

//

public class IcrKeyAvailableJob(
    IMultiTenantContainerAccessor tenantContainerAccessor,
    ILogger<IcrKeyAvailableJob> logger) : AbstractJob
{
    public IcrKeyAvailableJobData Data { get; set; } = new();

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
            
            var service = scope.Resolve<IcrKeyAvailableBackgroundService>();
            await service.Run(Data, cancellationToken);

            service.RunCount++;

            logger.LogDebug("IcrKeyAvailableJob RunCount: {rc}", service.RunCount);
            if (service.RunCount > 30) //TODO: config
            {
                service.RunCount = 0;
                return JobExecutionResult.Success();
            }

            return JobExecutionResult.Reschedule(DateTimeOffset.Now.AddSeconds(60));
        }
        catch (OdinSecurityException se)
        {
            logger.LogDebug(se, "IcrKeyUpgradeJob failed to use token");
            return JobExecutionResult.Abort();
        }
        catch (CryptographicException ce)
        {
            logger.LogDebug(ce, "IcrKeyUpgradeJob failed to decrypt");
            return JobExecutionResult.Abort();
        }
        catch (Exception e)
        {
            logger.LogError(e, "IcrKeyUpgradeJob failed to run");
            return JobExecutionResult.Fail();
        }
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
        Data = OdinSystemSerializer.DeserializeOrThrow<IcrKeyAvailableJobData>(json);
    }

    //
}