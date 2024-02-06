using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Quartz;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;
#nullable enable

public class DeleteTenantScheduler(ILogger<DeleteTenantScheduler> logger, string domain) : AbstractJobScheduler
{
    public override bool IsExclusive => true;
    public override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);
        var jobKey = jobBuilder.CreateUniqueJobKey<TJob>();

        jobBuilder
            .WithIdentity(jobKey)
            .WithRetry(2, TimeSpan.FromSeconds(1))
            .WithRetention(TimeSpan.FromDays(2))
            .UsingJobData("domain", domain);

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
                .WithPriority(1)
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

public class DeleteTenantJob(
    ICorrelationContext correlationContext,
    ILogger<DeleteTenantJob> logger,
    IIdentityRegistry identityRegistry) : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var domain = (string)context.JobDetail.JobDataMap["domain"];

        logger.LogDebug("Starting delete tenant {domain}", domain);
        var sw = Stopwatch.StartNew();
        await identityRegistry.ToggleDisabled(domain, true);
        await identityRegistry.DeleteRegistration(domain);
        logger.LogDebug("Finished delete tenant {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
    }
}

