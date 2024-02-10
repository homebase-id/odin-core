using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.CorrelationId;
using Quartz;

namespace Odin.Core.Services.Quartz;

public class DeleteJobDetailsScheduler : AbstractJobScheduler
{
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

    public sealed override string JobType { get; } = Helpers.UniqueId();

    public sealed override Task<(JobBuilder, List<TriggerBuilder>)> Schedule<TJob>(JobBuilder jobBuilder)
    {
        if (_jobToDelete.Group == jobBuilder.GetGroupName<DeleteJobDetailsJob>())
        {
            // Don't schedule a job to delete a deletion job => infinite loop
            return Task.FromResult((jobBuilder, new List<TriggerBuilder>()));
        }

        _logger.LogDebug("Scheduling {JobType}", typeof(TJob).Name);

        jobBuilder.UsingJobData(JobConstants.JobToDeleteKey, _jobToDelete.ToString());

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

public class DeleteJobDetailsJob(
    ICorrelationContext correlationContext,
    ILogger<DeleteJobDetailsJob> logger)
    : AbstractJob(correlationContext)
{
    protected sealed override async Task Run(IJobExecutionContext context)
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
                logger.LogDebug("Could not delete {JobKey}", jobToDelete);
            }
        }
    }
}
