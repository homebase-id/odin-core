using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Services.JobManagement;

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

    public static void ApplyCorrelationId(this IJobExecutionContext context, ICorrelationContext correlationContext)
    {
        if (context.JobDetail.Durable)
        {
            var jobData = context.JobDetail.JobDataMap;
            if (jobData.TryGetString(JobConstants.CorrelationIdKey, out var correlationId) && correlationId != null)
            {
                correlationContext.Id = correlationId;
            }
        }
    }

    //

    public static Task SetJobResponseData(
        this IJobExecutionContext context,
        object serializableObject)
    {
        return context.Scheduler.SetJobResponseData(context.JobDetail, serializableObject);
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
                var instance = ActivatorUtilities.CreateInstance(serviceProvider, type);
                if (instance is IJobEvent jobEvent)
                {
                    await jobEvent.Execute(context, status);
                }
            }
        }
    }

    //

}
