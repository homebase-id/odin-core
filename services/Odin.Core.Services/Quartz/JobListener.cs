using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Core.Services.Quartz;
#nullable enable

public class JobListener : IJobListener
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<JobListener> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IJobManager _jobManager;
    private readonly ICorrelationContext _correlationContext;

    public JobListener(
        IServiceProvider serviceProvider,
        ILogger<JobListener> logger,
        IJobManager jobManager,
        ILoggerFactory loggerFactory,
        ICorrelationContext correlationContext)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _jobManager = jobManager;
        _loggerFactory = loggerFactory;
        _correlationContext = correlationContext;
    }

    //

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        context.ApplyCorrelationId(_correlationContext);

        var job = context.JobDetail;
        var jobData = job.JobDataMap;

        _logger.LogDebug("Job {JobKey} starting", job.Key);
        if (job.Durable)
        {
            jobData[JobConstants.StatusKey] = JobConstants.StatusValueStarted;
            await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
        }

        await context.ExecuteJobEvent(_serviceProvider, JobStatus.Started);
    }

    //

    public async Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken)
    {
        context.ApplyCorrelationId(_correlationContext);

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

            await context.ExecuteJobEvent(_serviceProvider, JobStatus.Completed);
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

                var errorMessage = exception is OdinClientException
                    ? exception.Message
                    : $"Interal server error. Check logs around job {job.Key}";

                if (job.Durable)
                {
                    jobData[JobConstants.StatusKey] = JobConstants.StatusValueFailed;
                    jobData[JobConstants.JobErrorMessageKey] = errorMessage;
                    await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
                }

                if (jobData.TryGetString(JobConstants.FailedRetentionSecondsKey, out var retention) && retention != null)
                {
                    await ScheduleJobDeletion(job.Key, long.Parse(retention));
                }

                await context.ExecuteJobEvent(_serviceProvider, JobStatus.Failed);

                // SEB:TODO dead letter queue???
            }

        }

    }

    //

    private async Task ScheduleJobDeletion(JobKey jobKey, long retentionSeconds)
    {
        var deleteAt = DateTimeOffset.Now + TimeSpan.FromSeconds(retentionSeconds);
        var jobSchedule = new DeleteJobDetailsScheduler(_loggerFactory, jobKey, deleteAt);
        await _jobManager.Schedule<DeleteJobDetailsJob>(jobSchedule);
    }

    //


    //

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    //

    public string Name => nameof(JobListener);
}