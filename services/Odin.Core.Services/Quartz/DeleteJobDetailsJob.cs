using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Quartz;

namespace Odin.Core.Services.Quartz;

public class DeleteJobDetailsScheduler : IJobScheduler
{
    public bool IsExclusive => false;
    private readonly ILogger<DeleteJobDetailsScheduler> _logger;
    private readonly JobKey _jobToDelete;
    private readonly DateTimeOffset _deleteAt;

    public DeleteJobDetailsScheduler(ILoggerFactory loggerFactory, JobKey jobToDelete, DateTimeOffset deleteAt)
    {
        _logger = loggerFactory.CreateLogger<DeleteJobDetailsScheduler>();
        _jobToDelete = jobToDelete;
        _deleteAt = deleteAt;
    }

    //

    public DeleteJobDetailsScheduler(ILoggerFactory loggerFactory, JobKey jobToDelete, TimeSpan deleteAfter)
        : this(loggerFactory, jobToDelete, DateTimeOffset.Now + deleteAfter)
    {
    }

    //

    public async Task<JobKey> Schedule<TJob>(IScheduler scheduler) where TJob : IJob
    {
        if (_jobToDelete.Group == scheduler.GetGroupName<DeleteJobDetailsJob>())
        {
            // Don't schedule a job to delete a deletion job => infinite loop
            return _jobToDelete;
        }

        _logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        var jobKey = scheduler.CreateUniqueJobKey<TJob>();
        var job = JobBuilder.Create<TJob>()
            .WithIdentity(jobKey)
            .UsingJobData(JobConstants.JobToDeleteKey, _jobToDelete.ToString())
            .Build();
        var trigger = TriggerBuilder.Create()
            .StartAt(_deleteAt)
            .WithPriority(1)
            .Build();
        await scheduler.ScheduleJob(job, trigger);
        return jobKey;
    }
}

//

public class DeleteJobDetailsJob(ILogger<DeleteJobDetailsJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString(JobConstants.JobToDeleteKey, out var jobToDelete) && jobToDelete != null)
        {
            var scheduler = context.Scheduler;
            var jobKey = scheduler.ParseJobKey(jobToDelete);

            if (await scheduler.DeleteJob(jobKey))
            {
                logger.LogDebug("Deleted {JobKey}", jobToDelete);
            }
            else
            {
                logger.LogWarning("Failed to delete {JobKey}", jobToDelete);
            }
        }
    }
}
