using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;
#nullable enable

public class NonExclusiveTestSchedule(ILogger<NonExclusiveTestSchedule> logger) : OldAbstractOldIJobSchedule
{
    public sealed override string SchedulingKey { get; } = OldHelpers.UniqueId();
    public sealed override OldSchedulerGroup OldSchedulerGroup { get; } = OldSchedulerGroup.Default;

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

public class OldNonExclusiveTestJob(
    ICorrelationContext correlationContext,
    ILoggerFactory loggerFactory)
    : OldAbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var logger = loggerFactory.CreateLogger<OldNonExclusiveTestJob>();

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
