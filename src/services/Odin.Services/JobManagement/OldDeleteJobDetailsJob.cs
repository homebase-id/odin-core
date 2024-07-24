using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Services.JobManagement;

public class DeleteOldIJobDetailsSchedule : OldAbstractOldIJobSchedule
{
    private readonly ILogger<DeleteOldIJobDetailsSchedule> _logger;
    private readonly JobKey _jobToDelete;
    private readonly DateTimeOffset _deleteAt;

    public DeleteOldIJobDetailsSchedule(ILoggerFactory loggerFactory, JobKey jobToDelete, DateTimeOffset deleteAt)
    {
        _logger = loggerFactory.CreateLogger<DeleteOldIJobDetailsSchedule>();
        _jobToDelete = jobToDelete;
        _deleteAt = deleteAt;

        var schedulerGroup = jobToDelete.SchedulerGroup();
        if (schedulerGroup == null)
        {
            throw new JobManagerException($"Error getting scheduler group for {jobToDelete}");
        }
        OldSchedulerGroup = schedulerGroup.Value;
    }

    //

    public sealed override string SchedulingKey { get; } = OldHelpers.UniqueId();

    public sealed override OldSchedulerGroup OldSchedulerGroup { get; }

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        _logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        jobBuilder.UsingJobData(OldJobConstants.JobToDeleteKey, _jobToDelete.ToString());

        var triggerBuilders = new List<TriggerBuilder>
        {
            TriggerBuilder.Create()
                .StartAt(_deleteAt)
                .WithPriority(1)
        };

        return Task.FromResult((jobBuilder, triggerBuilders));
    }
}

//

public class OldDeleteJobDetailsJob(
    ICorrelationContext correlationContext,
    ILogger<OldDeleteJobDetailsJob> logger)
    : OldAbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.TryGetString(OldJobConstants.JobToDeleteKey, out var jobToDelete) && jobToDelete != null)
        {
            var scheduler = context.Scheduler;
            var jobKey = scheduler.ParseJobKey(jobToDelete);

            if (await scheduler.DeleteJob(jobKey))
            {
                logger.LogDebug("Deleted {JobKey}", jobToDelete);
            }
            else
            {
                logger.LogWarning("Could not delete {JobKey}", jobToDelete);
            }
        }
    }
}
