using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Quartz;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;
#nullable enable

public class ExclusiveTestScheduler(ILogger<ExclusiveTestScheduler> logger) : AbstractJobScheduler
{
    public override bool IsExclusive => true;
    public override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);
        var jobKey = jobBuilder.CreateUniqueJobKey<TJob>();

        jobBuilder
            .WithIdentity(jobKey)
            .WithRetry(2, TimeSpan.FromSeconds(1))
            .WithRetention(TimeSpan.FromMinutes(1));

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
                .WithPriority(500)
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//

public class ExclusiveTestJob(
    ICorrelationContext correlationContext,
    ILogger<ExclusiveTestJob> logger)
    : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var jobKey = context.JobDetail.Key;
        logger.LogDebug("Starting {JobKey}", jobKey);

        var sw = Stopwatch.StartNew();
        logger.LogDebug("Working...");
        await Task.Delay(TimeSpan.FromSeconds(1));

        //
        // Store job specific data:
        //
        var jobData = context.JobDetail.JobDataMap;
        jobData["my-job-was"] = "a-success-hurrah!";
        await context.Scheduler.AddJob(context.JobDetail, true); // update JobDataMap

        logger.LogDebug("Finished {JobKey} on thread {tid} in {elapsed}s", jobKey, Environment.CurrentManagedThreadId, sw.ElapsedMilliseconds / 1000.0);
    }
}

//