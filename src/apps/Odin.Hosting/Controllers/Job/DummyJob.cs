using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Controllers.Job;
#nullable enable

public class DummySchedule(string echo) : OldAbstractOldIJobSchedule
{
    public sealed override string SchedulingKey { get; } = OldHelpers.UniqueId();
    public override OldSchedulerGroup OldSchedulerGroup { get; } = OldSchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobBuilder
            .WithRetry(2, TimeSpan.FromSeconds(5))
            .WithRetention(TimeSpan.FromMinutes(1))
            .WithJobEvent<DummyEvent>()
            .UsingJobData("echo", echo);

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
                .WithPriority(1)
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//

public class DummyJob(ICorrelationContext correlationContext, ILogger<DummyJob> logger) : OldAbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString("echo", out var echo) && echo != null)
        {
            logger.LogInformation("DummyJob says: {echo}", echo);
            await SetJobResponseData(context, new DummyReponseData { Echo = echo });
        }
    }
}

//

public class DummyEvent(ILogger<DummyEvent> logger) : OldIJobEvent
{
    public Task Execute(IJobExecutionContext context, OldJobStatus status)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString("echo", out var echo))
        {
            logger.LogInformation("DummyEvent status:{status} echo:{echo}", status, echo);
        }
        return Task.CompletedTask;
    }
}

//

public class DummyReponseData
{
    public string? Echo { get; set; }
}

