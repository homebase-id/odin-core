using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;

public class JobMemoryCacheDemoSchedule(IJobMemoryCache jobMemoryCache) : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } = Helpers.UniqueId();
    public sealed override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobMemoryCache.Insert(JobKey, "my secret data that is only stored in memory", TimeSpan.FromMinutes(1));

        jobBuilder
            .WithRetention(TimeSpan.FromMinutes(1))
            .WithJobEvent<JobMemoryCacheDemoEvent>();

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

public class JobMemoryCacheDemoJob(
    ICorrelationContext correlationContext,
    IJobMemoryCache jobMemoryCache) : AbstractJob(correlationContext)
{
    protected override Task Run(IJobExecutionContext context)
    {
        jobMemoryCache.TryGet<string>(context, out var secret);
        if (secret != "my secret data that is only stored in memory")
        {
            throw new Exception("Job failed to retrieve secret from memory cache. This is a REAL error. Go fix it.");
        }

        return Task.CompletedTask;
    }
}

public class JobMemoryCacheDemoEvent(IJobMemoryCache jobMemoryCache, JobMemoryCacheDemoTestContainer testContainer) : IJobEvent
{
    public Task Execute(IJobExecutionContext context, JobStatus status)
    {
        jobMemoryCache.TryGet<string>(context, out var secret);
        if (secret != "my secret data that is only stored in memory")
        {
            throw new Exception("Job failed to retrieve secret from memory cache. This is a REAL error. Go fix it.");
        }

        testContainer.Secret = secret;
        return Task.CompletedTask;
    }
}

public class JobMemoryCacheDemoTestContainer
{
    public string Secret { get; set; } = "";
}
