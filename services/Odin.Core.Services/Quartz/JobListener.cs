using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public class JobListener : IJobListener
{
    private readonly ILogger<JobListener> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IJobSchedulerFactory _jobSchedulerFactory;

    public JobListener(
        ILogger<JobListener> logger,
        IJobSchedulerFactory jobSchedulerFactory,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _jobSchedulerFactory = jobSchedulerFactory;
        _loggerFactory = loggerFactory;
    }

    //

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        var job = context.JobDetail;
        if (job.Durable)
        {
            var jobData = job.JobDataMap;
            jobData[JobConstants.StatusKey] = JobConstants.StatusValueStarted;
            await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
        }
    }

    //

    public async Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken)
    {
        var job = context.JobDetail;
        var jobData = job.JobDataMap;

        if (jobException == null)
        {
            _logger.LogDebug("Job {JobKey} completed", job.Key);

            if (job.Durable)
            {
                jobData[JobConstants.StatusKey] = JobConstants.StatusValueCompleted;
                await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
            }

            if (jobData.TryGetString(JobConstants.CompletedRetentionSecondsKey, out var retention) && retention != null)
            {
                await ScheduleJobDeletion(job.Key, long.Parse(retention));
            }
        }
        else
        {
            var retryMax = 0;
            var retryDelaySeconds = 0L;
            var retry =
                jobData.TryGetIntValue(JobConstants.RetryCountKey, out var retryCount) &&
                jobData.TryGetIntValue(JobConstants.RetryMaxKey, out retryMax) &&
                jobData.TryGetLongValue(JobConstants.RetryDelaySecondsKey, out retryDelaySeconds);
            if (retry && retryCount < retryMax)
            {
                var retryAt = DateTimeOffset.Now + TimeSpan.FromSeconds(retryDelaySeconds);
                _logger.LogWarning("Job {JobKey} failed. Scheduling retry starting {retryAt}.", job.Key, retryAt);

                jobData[JobConstants.RetryCountKey] = (++retryCount).ToString();
                await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap

                var retryTrigger = TriggerBuilder.Create()
                    .StartAt(retryAt)
                    .ForJob(context.JobDetail)
                    .Build();
                await context.Scheduler.ScheduleJob(retryTrigger, cancellationToken);
            }
            else
            {
                Exception exception = jobException;
                while (exception.InnerException != null)
                {
                    exception = exception.InnerException;
                }
                _logger.LogError(exception, "Job {JobKey} failed: {error}", job.Key, exception.Message);

                if (job.Durable)
                {
                    jobData[JobConstants.StatusKey] = JobConstants.StatusValueFailed;
                    await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
                }

                if (jobData.TryGetString(JobConstants.FailedRetentionSecondsKey, out var retention) && retention != null)
                {
                    await ScheduleJobDeletion(job.Key, long.Parse(retention));
                }

                // SEB:TODO dead letter queue???
            }

        }

    }

    //

    private async Task ScheduleJobDeletion(JobKey jobKey, long retentionSeconds)
    {
        var deleteAt = DateTimeOffset.Now + TimeSpan.FromSeconds(retentionSeconds);
        var jobSchedule = new DeleteJobDetailsScheduler(_loggerFactory, jobKey, deleteAt);
        await _jobSchedulerFactory.Schedule<DeleteJobDetailsJob>(jobSchedule);
    }

    //

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    //

    public string Name => nameof(JobListener);


}