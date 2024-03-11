using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Services.JobManagement;

public class LogSchedule(string text) : AbstractJobSchedule
{
    public sealed override string SchedulingKey { get; } = Helpers.UniqueId();
    public override SchedulerGroup SchedulerGroup { get; } = SchedulerGroup.Default;

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        jobBuilder.UsingJobData("text", text);

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

public class LogJob(ICorrelationContext correlationContext, ILogger<LogJob> logger) : AbstractJob(correlationContext)
{
    protected sealed override Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString("text", out var text) && text != null)
        {
            logger.LogInformation("LogJob says: {text}", text);
        }
        return Task.CompletedTask;
    }
}
