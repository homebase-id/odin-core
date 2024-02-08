using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Nito.AsyncEx;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

// SEB:TODO
// - testing, see ExceptionHandlingMiddlewareTest.cs
// - deadletter
// - clean up ExclusiveJobManager and family
// - move NonExclusiveTestJob and ExclusiveTestJob to test project
// - IHostApplicationLifetime hostApplicationLifetime

public interface IJobManager
{
    Task<JobKey> Schedule<TJob>(AbstractJobScheduler jobScheduler) where TJob : IJob;
    Task<JobResponse> GetResponse(JobKey jobKey);
    Task<bool> Exists(JobKey jobKey);
    Task<bool> Delete(JobKey jobKey);
}

//

public sealed class JobManager(
    ILogger<JobManager> logger,
    ISchedulerFactory schedulerFactory,
    ICorrelationContext correlationContext) : IJobManager
{
    private readonly AsyncLock _mutex = new();

    //

    public async Task<JobKey> Schedule<TJob>(AbstractJobScheduler jobScheduler) where TJob : IJob
    {
        using (await _mutex.LockAsync())
        {
            var scheduler = await schedulerFactory.GetScheduler();

            var jobKey = await scheduler.GetScheduledJobKey(jobScheduler.JobId);
            if (jobKey != null)
            {
                logger.LogDebug("Already scheduled {JobType}: {JobKey}", typeof(TJob).Name, jobKey);
                return jobKey;
            }

            var (jobBuilder, triggerBuilders) = await jobScheduler.Schedule<TJob>(JobBuilder.Create<TJob>());
            if (triggerBuilders.Count == 0)
            {
                // We don't want to schedule a job without triggers (e.g. a deletion-deletion job)
                return new JobKey("non-scheduled-job");
            }

            var jobName = Helpers.UniqueId();
            if (jobName.Contains('.'))
            {
                throw new ArgumentException("Job name must not contain '.'");
            }

            jobKey = new JobKey(jobName, jobScheduler.JobId);
            jobBuilder.WithIdentity(jobKey);
            jobBuilder.UsingJobData(JobConstants.CorrelationIdKey, correlationContext.Id);

            var job = jobBuilder.Build();
            foreach (var triggerBuilder in triggerBuilders)
            {
                var trigger = triggerBuilder.Build();
                await scheduler.ScheduleJob(job, trigger);
            }

            logger.LogDebug("Scheduled {JobType}: {JobKey}", typeof(TJob).Name, jobKey);
            return jobKey;
        }
    }

    //

    public async Task<JobResponse> GetResponse(JobKey jobKey)
    {
        var scheduler = await schedulerFactory.GetScheduler();
        var job = await scheduler.GetJobDetail(jobKey);

        if (job == null || !job.Key.Equals(jobKey))
        {
            return new JobResponse
            {
                Status = JobStatusEnum.NotFound,
                JobKey = jobKey.ToString(),
            };
        }

        var jobData = job.JobDataMap;
        jobData.TryGetString(JobConstants.StatusKey, out var status);
        jobData.TryGetString(JobConstants.ErrorMessageKey, out var errorMessage);
        jobData.TryGetString(JobConstants.UserDefinedDataKey, out var data);

        var jobResponse = new JobResponse
        {
            Status = Helpers.JobStatusFromStatusValue(status ?? ""),
            JobKey = jobKey.ToString(),
            Error = errorMessage,
            Data = data,
        };

        return jobResponse;
    }

    //

    public async Task<bool> Exists(JobKey jobKey)
    {
        using (await _mutex.LockAsync())
        {
            var scheduler = await schedulerFactory.GetScheduler();
            return await scheduler.CheckExists(jobKey);
        }
    }

    //

    public async Task<bool> Delete(JobKey jobKey)
    {
        using (await _mutex.LockAsync())
        {
            var scheduler = await schedulerFactory.GetScheduler();
            var deleted = await scheduler.DeleteJob(jobKey);
            if (deleted)
            {
                logger.LogDebug("Explicitly deleted {JobKey}", jobKey);
            }
            return deleted;
        }
    }

    //

}
