using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;
#nullable enable

public class NonExclusiveTestSchedule(ILogger<NonExclusiveTestSchedule> logger) : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } = Helpers.UniqueId();
    public sealed override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        jobBuilder
            .WithRetry(2, TimeSpan.FromSeconds(1))
            .WithRetention(TimeSpan.FromMinutes(1))
            .UsingJobData("echo", TestEcho);

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }

    public string TestEcho { get; set; } = "";
}

//

public class NonExclusiveTestJob(
    ICorrelationContext correlationContext,
    ILoggerFactory loggerFactory)
    : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var logger = loggerFactory.CreateLogger<NonExclusiveTestJob>();

        var jobKey = context.JobDetail.Key;
        logger.LogDebug("Starting {JobKey}", jobKey);
        logger.LogDebug("Working...");

        var jobData = context.JobDetail.JobDataMap;
        jobData.TryGetString("echo", out var echo);

        await SetJobResponseData(context, new NonExclusiveTestData { Echo = echo });

        logger.LogDebug("Finished {JobKey}", jobKey);
    }
}

//

public class NonExclusiveTestData
{
    public string? Echo { get; set; }
}
