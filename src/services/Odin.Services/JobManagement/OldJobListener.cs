using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Services.JobManagement;
#nullable enable

public class OldJobListener(
    IServiceProvider serviceProvider,
    ILogger<OldJobListener> logger,
    ILoggerFactory loggerFactory,
    ICorrelationContext correlationContext,
    IJobMemoryCache jobMemoryCache)
    : IJobListener
{
    //

    public async Task JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        context.ApplyCorrelationId(correlationContext);

        var job = context.JobDetail;
        var jobData = job.JobDataMap;

        logger.LogDebug("Job {JobKey} starting", job.Key);
        if (job.Durable)
        {
            jobData[OldJobConstants.StatusKey] = OldJobConstants.StatusValueStarted;
            await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
        }

        await context.ExecuteJobEvent(serviceProvider, OldJobStatus.Started);
    }

    //

    public async Task JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken)
    {
        context.ApplyCorrelationId(correlationContext);

        var job = context.JobDetail;
        var jobData = job.JobDataMap;

        if (jobException == null)
        {
            try
            {
                logger.LogDebug("Job {JobKey} completed", job.Key);

                if (job.Durable)
                {
                    jobData[OldJobConstants.StatusKey] = OldJobConstants.StatusValueCompleted;
                    await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
                }

                await ScheduleJobDeletion(context, OldJobConstants.CompletedRetentionSecondsKey);
                await context.ExecuteJobEvent(serviceProvider, OldJobStatus.Completed);
            }
            finally
            {
                jobMemoryCache.Remove(job.Key);
            }
        }
        else
        {
            var retryMax = 0;
            var retryDelaySeconds = 0L;
            var retry =
                jobData.TryGetIntValue(OldJobConstants.RetryCountKey, out var retryCount) &&
                jobData.TryGetIntValue(OldJobConstants.RetryMaxKey, out retryMax) &&
                jobData.TryGetLongValue(OldJobConstants.RetryDelaySecondsKey, out retryDelaySeconds);
            if (retry && retryCount < retryMax)
            {
                retryCount++;
                var retryAt = DateTimeOffset.Now + TimeSpan.FromSeconds(retryDelaySeconds);
                logger.LogWarning("Job {JobKey} unsuccessful. Scheduling retry ({retryCount}/{retryMax}) starting {retryAt}.",
                    job.Key, retryCount, retryMax, retryAt);

                jobData[OldJobConstants.RetryCountKey] = retryCount.ToString();
                await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap

                var retryTrigger = TriggerBuilder.Create()
                    .StartAt(retryAt)
                    .ForJob(context.JobDetail)
                    .Build();
                await context.Scheduler.ScheduleJob(retryTrigger, cancellationToken);
            }
            else
            {
                try
                {
                    Exception exception = jobException;
                    while (exception.InnerException != null)
                    {
                        exception = exception.InnerException;
                    }

                    logger.LogError(exception, "Job {JobKey} unsuccessful: {error}", job.Key, exception.Message);

                    var errorMessage = exception is OdinClientException
                        ? exception.Message
                        : $"Internal server error. Check logs around job {job.Key}";

                    if (job.Durable)
                    {
                        jobData[OldJobConstants.StatusKey] = OldJobConstants.StatusValueFailed;
                        jobData[OldJobConstants.JobErrorMessageKey] = errorMessage;
                        await context.Scheduler.AddJob(context.JobDetail, true, cancellationToken); // update JobDataMap
                    }

                    await ScheduleJobDeletion(context, OldJobConstants.FailedRetentionSecondsKey);
                    await context.ExecuteJobEvent(serviceProvider, OldJobStatus.Failed);
                }
                finally
                {
                    jobMemoryCache.Remove(job.Key);
                }
            }
        }
    }

    //

    private async Task ScheduleJobDeletion(IJobExecutionContext context, string retentionKey)
    {
        var job = context.JobDetail;
        var jobData = job.JobDataMap;

        if (jobData.TryGetString(OldJobConstants.JobTypeName, out var jobTypeName) && jobTypeName != null)
        {
            // Don't schedule a job to delete a deletion job => infinite loop
            if (jobTypeName == typeof(OldDeleteJobDetailsJob).FullName)
            {
                return;
            }
        }

        if (jobData.TryGetString(retentionKey, out var retention) && retention != null)
        {
            var jobManager = serviceProvider.GetRequiredService<IOldJobManager>();
            var deleteAt = DateTimeOffset.Now + TimeSpan.FromSeconds(long.Parse(retention));
            var jobSchedule = new DeleteOldIJobDetailsSchedule(loggerFactory, job.Key, deleteAt);
            await jobManager.Schedule<OldDeleteJobDetailsJob>(jobSchedule);
        }
    }

    //


    //

    public Task JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    //

    public string Name => nameof(OldJobListener);
}