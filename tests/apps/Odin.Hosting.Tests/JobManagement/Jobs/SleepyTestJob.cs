using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;
#nullable enable

public class SleepyTestSchedule(ILogger<SleepyTestSchedule> logger, SchedulerGroup schedulerGroup) : AbstractJobSchedule
{
    public sealed override string SchedulingKey => Helpers.UniqueId(); // computed property, since we want a new one each time for these specific tests
    public sealed override SchedulerGroup SchedulerGroup { get; } = schedulerGroup;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        jobBuilder
            .WithRetry(2, TimeSpan.FromSeconds(1))
            .WithRetention(TimeSpan.FromMinutes(1))
            .UsingJobData("echo", TestEcho)
            .UsingJobData("sleep", SleepTime.ToString());

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }

    public int SleepTime { get; set; } = 1000;
    public string TestEcho { get; set; } = "";
}

//

public class SleepyTestJob(
    ICorrelationContext correlationContext,
    ILoggerFactory loggerFactory)
    : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var logger = loggerFactory.CreateLogger<SleepyTestJob>();

        var jobKey = context.JobDetail.Key;
        logger.LogDebug("Starting {JobKey}", jobKey);

        var jobData = context.JobDetail.JobDataMap;
        jobData.TryGetString("echo", out var echo);
        jobData.TryGetInt("sleep", out var sleep);

        await Task.Delay(sleep);

        await SetJobResponseData(context, new SleepyTestData { Echo = echo });
    }
}

//

public class SleepyTestData
{
    public string? Echo { get; set; }
}