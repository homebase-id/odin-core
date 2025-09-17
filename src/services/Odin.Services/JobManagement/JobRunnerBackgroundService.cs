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
        await Task.WhenAll(tasks);
    }
    
    //
    
    private static TimeSpan CalculateSleepDuration(long? nextRun)
    {
        if (nextRun == null)
        {
            return MaxSleepDuration;
        }
        
        var now = DateTimeOffset.Now;
        var nextRunTime = DateTimeOffset.FromUnixTimeMilliseconds(nextRun.Value);
        
        if (nextRunTime < now)
        {
            return TimeSpan.Zero;
        }

        var duration = nextRunTime - now;
        return duration > MaxSleepDuration ? MaxSleepDuration : duration;
    }
    
    //
}
