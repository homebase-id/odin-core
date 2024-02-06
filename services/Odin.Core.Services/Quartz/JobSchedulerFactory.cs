using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public interface IJobSchedulerFactory
{
    Task<JobKey> Schedule<TJob>(AbstractJobScheduler jobScheduler) where TJob : IJob;
}

//

// SEB:TODO testing, see ExceptionHandlingMiddlewareTest.cs

public sealed class JobSchedulerFactory(
    ILogger<JobSchedulerFactory> logger,
    ISchedulerFactory schedulerFactory,
    ICorrelationContext correlationContext) : IJobSchedulerFactory
{
    private readonly AsyncLock _mutex = new();

    public async Task<JobKey> Schedule<TJob>(AbstractJobScheduler jobScheduler) where TJob : IJob
    {
        using (await _mutex.LockAsync())
        {
            var scheduler = await schedulerFactory.GetScheduler();
            if (jobScheduler.IsExclusive)
            {
                var jobKey = await scheduler.GetScheduledJobKey<TJob>();
                if (jobKey != null)
                {
                    logger.LogDebug("Already scheduled {JobType}: {JobKey}", typeof(TJob).Name, jobKey);
                    return jobKey;
                }
            }

            var (jobBuilder, triggerBuilders) = await jobScheduler.Schedule<TJob>(JobBuilder.Create<TJob>());
            if (triggerBuilders.Count == 0)
            {
                // We don't want to schedule a job without triggers (e.g. a deletion-deletion job)
                return new JobKey("non-scheduled-job");
            }

            jobBuilder.UsingJobData(JobConstants.CorrelationIdKey, correlationContext.Id);

            var job = jobBuilder.Build();
            foreach (var triggerBuilder in triggerBuilders)
            {
                var trigger = triggerBuilder.Build();
                await scheduler.ScheduleJob(job, trigger);
            }

            logger.LogDebug("Scheduled {JobType}: {JobKey}", typeof(TJob).Name, job.Key);
            return job.Key;
        }
    }
}
