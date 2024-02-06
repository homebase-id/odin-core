using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Services.Quartz;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;
#nullable enable


public class NonExclusiveTestScheduler(ILogger<NonExclusiveTestScheduler> logger) : IJobScheduler
{
    public bool IsExclusive => false;

    public async Task<JobKey> Schedule<TJob>(IScheduler scheduler) where TJob : IJob
    {
        logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        var jobKey = scheduler.CreateUniqueJobKey<TJob>();
        var job = JobBuilder.Create<TJob>()
            .WithIdentity(jobKey)
            .WithRetry(2, TimeSpan.FromSeconds(1))
            .WithRetention(TimeSpan.FromMinutes(1))
            // .UsingJobData("domain", "whatever")
            .Build();
        var trigger = TriggerBuilder.Create()
            //.StartNow()
            .StartAt(DateTimeOffset.Now + TimeSpan.FromSeconds(1))
            .WithPriority(1)
            .Build();
        await scheduler.ScheduleJob(job, trigger);
        return jobKey;
    }
}

//

public class NonExclusiveTestJob(ILogger<NonExclusiveTestJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var jobKey = context.JobDetail.Key;

        logger.LogDebug("Starting {JobKey}", jobKey);

        var sw = Stopwatch.StartNew();
        logger.LogDebug("Working...");
        await Task.Delay(TimeSpan.FromSeconds(1));

        // throw new OdinSystemException("Oh no...!");

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

