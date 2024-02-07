using System.Threading.Tasks;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Core.Services.Quartz;

public abstract class AbstractJob(ICorrelationContext correlationContext) : IJob
{
    // Consumer must implement this method
    protected abstract Task Run(IJobExecutionContext context);

    //

    // Called by Quartz
    public async Task Execute(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;

        var correlationId = jobData.GetString(JobConstants.CorrelationIdKey);
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            correlationContext.Id = correlationId;
        }

        await Run(context);
    }

    //

    protected static Task SetUserDefinedJobData(IJobExecutionContext context, object serializableObject)
    {
        return context.Scheduler.SetUserDefinedJobData(context.JobDetail, serializableObject);
    }
}
