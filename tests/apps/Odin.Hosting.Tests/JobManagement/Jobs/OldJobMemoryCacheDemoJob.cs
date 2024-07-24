using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;

public class OldIJobMemoryCacheDemoSchedule(IJobMemoryCache jobMemoryCache) : OldAbstractOldIJobSchedule
{
    public sealed override string SchedulingKey { get; } = OldHelpers.UniqueId();
    public sealed override OldSchedulerGroup OldSchedulerGroup { get; } = OldSchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobMemoryCache.Insert(JobKey, "my secret data that is only stored in memory", TimeSpan.FromMinutes(1));

        jobBuilder
            .WithRetention(TimeSpan.FromMinutes(1))
            .WithJobEvent<OldIJobMemoryCacheDemoEvent>();

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

public class OldJobMemoryCacheDemoJob(
    ICorrelationContext correlationContext,
    IJobMemoryCache jobMemoryCache) : OldAbstractJob(correlationContext)
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

public class OldIJobMemoryCacheDemoEvent(IJobMemoryCache jobMemoryCache, JobMemoryCacheDemoTestContainer testContainer) : OldIJobEvent
{
    public Task Execute(IJobExecutionContext context, OldJobStatus status)
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
