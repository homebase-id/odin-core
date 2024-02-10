using System.Threading.Tasks;
using Quartz;

namespace Odin.Core.Services.Quartz;

public static class JobExecutionContextExtensions
{
    public static Task UpdateJobMap(
        this IJobExecutionContext context,
        string key,
        string value)
    {
        return context.Scheduler.UpdateJobMap(context.JobDetail, key, value);
    }

    public static Task SetUserDefinedJobData(
        this IJobExecutionContext context,
        object serializableObject)
    {
        return context.Scheduler.SetUserDefinedJobData(context.JobDetail, serializableObject);
    }

}
