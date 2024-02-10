using System;
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

    //

    public static Task SetUserDefinedJobData(
        this IJobExecutionContext context,
        object serializableObject)
    {
        return context.Scheduler.SetUserDefinedJobData(context.JobDetail, serializableObject);
    }

    //

    public static async Task ExecuteJobEvent(
        this IJobExecutionContext context,
        IServiceProvider serviceProvider,
        JobStatus status)
    {
        var jobData = context.JobDetail.JobDataMap;

        if (jobData.TryGetString(JobConstants.JobEventTypeKey, out var eventType) && eventType != null)
        {
            var type = Type.GetType(eventType);
            if (type != null)
            {
                var instance = Activator.CreateInstance(type);
                if (instance is IJobEvent jobEvent)
                {
                    await jobEvent.Execute(serviceProvider, context, status);
                }
            }
        }
    }

    //

}
