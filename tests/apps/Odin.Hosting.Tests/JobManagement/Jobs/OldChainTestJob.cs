using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Logging.CorrelationId;
using Odin.Services.JobManagement;
using Quartz;

namespace Odin.Hosting.Tests.JobManagement.Jobs;
#nullable enable

public class ChainTestSchedule : OldAbstractOldIJobSchedule
{
    public sealed override string SchedulingKey { get; } = OldHelpers.UniqueId();
    public sealed override OldSchedulerGroup OldSchedulerGroup { get; } = OldSchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobBuilder
            .WithRetention(TimeSpan.FromMinutes(1))
            .UsingJobData("currentIteration", IterationCount.ToString());

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartNow()
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }

    public int IterationCount { get; set; } = 3;
}

//

public class OldChainTestJob(ICorrelationContext correlationContext, IJobManager jobManager)
    : OldAbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        jobData.TryGetInt("currentIteration", out var currentIteration);

        var nextJobKey = "";
        if (currentIteration > 1)
        {
            var scheduler = new ChainTestSchedule
            {
                IterationCount = currentIteration - 1
            };
            var jobKey = await jobManager.Schedule<OldChainTestJob>(scheduler);
            nextJobKey = jobKey.ToString();
        }

        await SetJobResponseData(context, new ChainTestData
        {
            IterationCount = currentIteration,
            NextJobKey = nextJobKey
        });
    }
}

//

public class ChainTestData
{
    public string NextJobKey { get; set; } = "";
    public int IterationCount { get; set; }
}