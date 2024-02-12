using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Odin.Core.Services.Quartz;
using Quartz;

namespace Odin.Hosting.Controllers.Job;
#nullable enable

public class DummyScheduler(string echo) : AbstractJobScheduler
{
    public sealed override string SchedulingKey { get; } = Helpers.UniqueId();

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

public class DummyJob(ICorrelationContext correlationContext, ILogger<LogJob> logger) : AbstractJob(correlationContext)
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

public class DummyEvent(ILogger<DummyEvent> logger) : IJobEvent
{
    public Task Execute(IJobExecutionContext context, JobStatus status)
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

