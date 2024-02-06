using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public interface IJobSchedulerFactory
{
    Task<JobKey> Schedule<TJob>(IJobScheduler jobScheduler) where TJob : IJob;
}

//

// SEB:TODO testing, see ExceptionHandlingMiddlewareTest.cs

public sealed class JobSchedulerFactory(
    ILogger<JobSchedulerFactory> logger,
    ISchedulerFactory schedulerFactory) : IJobSchedulerFactory
{
    private readonly AsyncLock _mutex = new();

    public async Task<JobKey> Schedule<TJob>(IJobScheduler jobScheduler) where TJob : IJob
    {
        using (await _mutex.LockAsync())
        {
            JobKey? jobKey;
            var scheduler = await schedulerFactory.GetScheduler();
            if (jobScheduler.IsExclusive)
            {
                jobKey = await scheduler.GetScheduledJobKey<TJob>();
                if (jobKey != null)
                {
                    logger.LogDebug("Already scheduled {JobType}: {JobKey}", typeof(TJob).Name, jobKey);
                    return jobKey;
                }
            }
            jobKey = await jobScheduler.Schedule<TJob>(scheduler);
            logger.LogDebug("Scheduled {JobType}: {JobKey}", typeof(TJob).Name, jobKey);
            return jobKey;
        }
    }
}
