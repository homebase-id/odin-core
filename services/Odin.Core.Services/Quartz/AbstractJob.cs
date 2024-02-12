using System;
using System.Threading.Tasks;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public abstract class AbstractJob(ICorrelationContext correlationContext) : IJob
{
    // Consumer must implement this method
    protected abstract Task Run(IJobExecutionContext context);

    //

    // How long to keep job if completed
    protected TimeSpan? CompletedRetention { get; private set; }

    // How long to keep job if failed
    protected TimeSpan? FailedRetention { get; private set; }

    //

    protected static Task SetJobResponseData(IJobExecutionContext context, object serializableObject)
    {
        return context.Scheduler.SetJobResponseData(context.JobDetail, serializableObject);
    }

    //

    // Called by Quartz
    public async Task Execute(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;

        context.ApplyCorrelationId(correlationContext);

        if (jobData.TryGetString(JobConstants.CompletedRetentionSecondsKey, out var cr) && cr != null)
        {
            CompletedRetention = TimeSpan.FromSeconds(long.Parse(cr));
        }

        if (jobData.TryGetString(JobConstants.FailedRetentionSecondsKey, out var fr) && fr != null)
        {
            FailedRetention = TimeSpan.FromSeconds(long.Parse(fr));
        }

        await Run(context);
    }

}
