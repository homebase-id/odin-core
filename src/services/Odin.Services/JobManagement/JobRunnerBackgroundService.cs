using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.JobManagement;

public class JobRunnerBackgroundService(
    ILogger<JobRunnerBackgroundService> logger,
    TableJobs tableJobs,
    IJobManager jobManager) : AbstractBackgroundService(logger)
{

    //

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var tasks = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                logger.LogDebug("{service} is running", GetType().Name);

                while (!stoppingToken.IsCancellationRequested && await tableJobs.GetNextScheduledJobAsync() is { } job)
                {
                    var task = jobManager.RunJobNowAsync(job.id, stoppingToken);
                    tasks.Add(task);
                }

                tasks.RemoveAll(t => t.IsCompleted);

                if (!stoppingToken.IsCancellationRequested)
                {
                    var sleepDuration = CalculateSleepDuration(await tableJobs.GetNextRunTimeAsync());
                    logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, sleepDuration);
                    await SleepAsync(sleepDuration, stoppingToken);
                }
            }
        }
        finally
        {
            // Drain in-flight jobs before exiting, even if OperationCanceledException was thrown somewhere
            await Task.WhenAll(tasks);
        }
    }
    
    //
    
    // A job can be written by something that cannot wake this runner: the import command line (the payload
    // move it schedules) or another node. So look at the table at least this often, however far off the next
    // known job is.
    private static readonly TimeSpan MaxPollInterval = TimeSpan.FromMinutes(1);

    private static TimeSpan CalculateSleepDuration(long? nextRun)
    {
        if (nextRun == null)
        {
            return MaxPollInterval;
        }
        
        var now = DateTimeOffset.Now;
        var nextRunTime = DateTimeOffset.FromUnixTimeMilliseconds(nextRun.Value);
        
        if (nextRunTime < now)
        {
            return TimeSpan.Zero;
        }

        var duration = nextRunTime - now;
        return duration > MaxPollInterval ? MaxPollInterval : duration;
    }
    
    //
}
