using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Quartz;
using Quartz;

namespace Odin.Core.Services.Admin.Tenants.Jobs;
#nullable enable


public class NonExclusiveTestScheduler(ILogger<NonExclusiveTestScheduler> logger) : AbstractJobScheduler
{
    public sealed override string JobId => Helpers.UniqueId();

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        jobBuilder
            .WithRetry(2, TimeSpan.FromSeconds(1))
            .WithRetention(TimeSpan.FromMinutes(1))
            .UsingJobData("foo", "bar");

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartAt(DateTimeOffset.Now + TimeSpan.FromSeconds(1))
                .WithPriority(1)
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//

public class NonExclusiveTestJob(
    ICorrelationContext correlationContext,
    ILoggerFactory loggerFactory,
    IJobManager jobManager)
    : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var logger = loggerFactory.CreateLogger<NonExclusiveTestJob>();

        var jobKey = context.JobDetail.Key;
        logger.LogDebug("Starting {JobKey}", jobKey);

        var sw = Stopwatch.StartNew();
        logger.LogDebug("Working...");
        await Task.Delay(TimeSpan.FromSeconds(1));

        // throw new OdinClientException("Oh no...!");

        //
        // Store job specific data:
        //
        var responseData = new
        {
            foo = "bar"
        };
        await SetUserDefinedJobData(context, responseData);

        var jobSchedule = new LogScheduler("NonExclusiveTestJob finished");
        await jobManager.Schedule<LogJob>(jobSchedule);

        logger.LogDebug("Finished {JobKey} on thread {tid} in {elapsed}s", jobKey, Environment.CurrentManagedThreadId, sw.ElapsedMilliseconds / 1000.0);
    }
}

//

