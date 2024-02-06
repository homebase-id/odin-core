using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Services.Quartz;
using Odin.Core.Services.Registry;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;
#nullable enable

public class DeleteTenantScheduler(ILogger<DeleteTenantScheduler> logger, string domain) : IJobScheduler
{
    public bool IsExclusive => true;

    public async Task<JobKey> Schedule<TJob>(IScheduler scheduler) where TJob : IJob
    {
        logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        var jobKey = scheduler.CreateUniqueJobKey<TJob>();
        var job = JobBuilder.Create<TJob>()
            .WithIdentity(jobKey)
            .WithRetry(2, TimeSpan.FromSeconds(5))
            .WithRetention(TimeSpan.FromDays(2))
            .UsingJobData("domain", domain)
            .Build();
        var trigger = TriggerBuilder.Create()
            .StartNow()
            .WithPriority(1)
            .Build();
        await scheduler.ScheduleJob(job, trigger);
        return jobKey;
    }
}

public class DeleteTenantJob(ILogger<DeleteTenantJob> logger, IIdentityRegistry identityRegistry) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var domain = (string)context.JobDetail.JobDataMap["domain"];

        logger.LogDebug("Starting delete tenant {domain}", domain);
        var sw = Stopwatch.StartNew();
        await identityRegistry.ToggleDisabled(domain, true);
        await identityRegistry.DeleteRegistration(domain);
        logger.LogDebug("Finished delete tenant {domain} in {elapsed}s", domain, sw.ElapsedMilliseconds / 1000.0);
    }
}

