using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;

public class EventDemoSchedule : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } = Helpers.UniqueId();
    public sealed override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobBuilder
            .WithRetention(TimeSpan.FromMinutes(1))
            .WithJobEvent<EventDemoEvent>()
            .UsingJobData("shouldFail", ShouldFail.ToString());

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }

    public bool ShouldFail { get; set; }
}


public class EventDemoJob(ICorrelationContext correlationContext) : AbstractJob(correlationContext)
{
    protected override Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;

        if (jobData.TryGetBooleanValue("shouldFail", out var shouldFail) && shouldFail)
        {
            throw new Exception("Job failed");
        }

        return Task.CompletedTask;
    }
}

public class EventDemoEvent(EventDemoTestContainer testContainer) : IJobEvent
{
    public Task Execute(IJobExecutionContext context, JobStatus status)
    {
        testContainer.Status.Add(status);
        return Task.CompletedTask;
    }
}

public class EventDemoTestContainer
{
    public List<JobStatus> Status { get; set; } = [];
}
