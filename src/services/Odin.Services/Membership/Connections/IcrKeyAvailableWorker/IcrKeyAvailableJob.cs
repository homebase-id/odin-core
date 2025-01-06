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
using Odin.Services.JobManagement.Jobs;
using Odin.Services.Tenant.Container;

namespace Odin.Services.Membership.Connections.IcrKeyAvailableWorker;

#nullable enable

//

public class IcrKeyAvailableJob(
    IMultiTenantContainerAccessor tenantContainerAccessor,
    ILogger<IcrKeyAvailableJob> logger) : AbstractJob
{
    public static readonly Guid JobTypeId = Guid.Parse("59d25227-25c1-4b26-b1e6-c50612eb15e3");
    public override string JobType => JobTypeId.ToString();

    public IcrKeyAvailableJobData Data { get; set; } = new();

    public override async Task<JobExecutionResult> Run(CancellationToken cancellationToken)
    {
        logger.LogDebug("IcrKeyAvailableJob - Running tenant [{t}]", Data.Tenant);

        try
        {
            if (!OdinId.IsValid(Data.Tenant))
            {
                logger.LogError("IcrKeyAvailableJob received empty tenant; aborting");
                return JobExecutionResult.Abort();
            }

            // Create a new lifetime scope for the tenant so db connections are isolated
            await using var scope = tenantContainerAccessor.Container().GetTenantScope(Data.Tenant!)
                .BeginLifetimeScope($"IcrKeyAvailableJob:Run:{Data.Tenant}:{Guid.NewGuid()}");

            var stickyHostnameContext = scope.Resolve<IStickyHostname>();
            stickyHostnameContext.Hostname = $"{Data.Tenant}&";
            var service = scope.Resolve<IcrKeyAvailableBackgroundService>();
            await service.Run(Data, cancellationToken);

            service.RunCount++;

            logger.LogDebug("IcrKeyAvailableJob RunCount: {rc}", service.RunCount);
            if (service.RunCount > 30) //TODO: config
            {
                logger.LogDebug("IcrKeyAvailableJob RunCount Complete; returning JobExecutionResult.Success");
                service.RunCount = 0;
                return JobExecutionResult.Success();
            }

            const int seconds = 60;
            logger.LogDebug("IcrKeyAvailableJob - rescheduled for {seconds} seconds", seconds);
            return JobExecutionResult.Reschedule(DateTimeOffset.Now.AddSeconds(seconds));
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
        var text = JobType + "X" + Data.Tenant;
        logger.LogDebug("IcrKeyUpgradeJob CreateJobHash {t}", text);
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