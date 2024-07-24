using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Odin.Services.Registry;
using Quartz;

namespace Odin.Services.Admin.Tenants.Jobs;
#nullable enable

public class DeleteTenantSchedule(ILogger<DeleteTenantSchedule> logger, string domain) : OldAbstractOldIJobSchedule
{
    public sealed override string SchedulingKey { get; } = $"delete-tenant:{domain.Replace('.', '_')}";
    public sealed override OldSchedulerGroup OldSchedulerGroup { get; } = OldSchedulerGroup.SlowLowPriority;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {Job}", SchedulingKey);

        jobBuilder
            .WithRetry(2, TimeSpan.FromSeconds(5))
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
    IIdentityRegistry identityRegistry) : OldAbstractJob(correlationContext)
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

