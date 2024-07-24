using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;

public class EventDemoSchedule : OldAbstractOldIJobSchedule
{
    public sealed override string SchedulingKey { get; } = OldHelpers.UniqueId();
    public sealed override OldSchedulerGroup OldSchedulerGroup { get; } = OldSchedulerGroup.Default;

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


public class OldEventDemoJob(ICorrelationContext correlationContext) : OldAbstractJob(correlationContext)
{
    protected override Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;

        if (jobData.TryGetBooleanValue("shouldFail", out var shouldFail) && shouldFail)
        {
            throw new Exception("Job threw exception on purpose. This is not an error if you spot this in the logs.");
        }

        return Task.CompletedTask;
    }
}

public class EventDemoEvent(EventDemoTestContainer testContainer) : OldIJobEvent
{
    public Task Execute(IJobExecutionContext context, OldJobStatus status)
    {
        testContainer.Status.Add(status);
        return Task.CompletedTask;
    }
}

public class EventDemoTestContainer
{
    public List<OldJobStatus> Status { get; set; } = [];
}
