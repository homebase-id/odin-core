using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;
#nullable enable

public class ExclusiveTestSchedule(ILogger<ExclusiveTestSchedule> logger) : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } = "exclusive-test";
    public sealed override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {Job}", SchedulingKey);

        if (RetryCount > 0)
        {
            jobBuilder.WithRetry(RetryCount, TimeSpan.FromSeconds(0));
        }

        if (Retention.TotalSeconds > 0)
        {
            jobBuilder.WithRetention(Retention);
        }

        jobBuilder
            .UsingJobData("echo", TestEcho)
            .UsingJobData("failCount", FailCount.ToString());

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }

    public string TestEcho { get; set; } = "";
    public int FailCount { get; set; } = 0;
    public int RetryCount { get; set; } = 0;
    public TimeSpan Retention { get; set; } = TimeSpan.FromMinutes(1);
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

        var jobData = context.JobDetail.JobDataMap;
        jobData.TryGetString("echo", out var echo);
        jobData.TryGetInt("failCount", out var failCount);

        if (context.JobDetail.Durable)
        {
            await context.UpdateJobMap("failCount", (failCount - 1).ToString());
        }

        if (failCount > 0)
        {
            throw new Exception("Job threw exception on purpose. This is not an error if you spot this in the logs.");
        }

        if (context.JobDetail.Durable)
        {
            await SetJobResponseData(context, new NonExclusiveTestData { Echo = echo });
        }

        logger.LogDebug("Finished {JobKey}", jobKey);
    }
}

//

public class ExclusiveTestData
{
    public string? Echo { get; set; }
}
