using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Odin.Services.Background.Services;
using Odin.Services.Base;

namespace Odin.Services.JobManagement;

public class JobRunnerBackgroundService(
    ILogger<JobRunnerBackgroundService> logger,
    IServiceProvider serviceProvider,
    ServerSystemStorage serverSystemStorage) : AbstractBackgroundService(logger)
{
    private IJobManager JobManager => serviceProvider.GetRequiredService<IJobManager>(); // avoids circular dependency 

    //
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var jobManager = JobManager;
        var jobs = serverSystemStorage.Jobs;
        
        var tasks = new List<Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);
        
            TimeSpan sleepDuration;
            using (var cn = serverSystemStorage.CreateConnection())
            {
                while (!stoppingToken.IsCancellationRequested && await jobs.GetNextScheduledJob(cn) is { } job)
                {
                    var task = jobManager.RunJobNowAsync(job.id, stoppingToken);
                    tasks.Add(task);
                }
            
                sleepDuration = CalculateSleepDuration(await jobs.GetNextRunTime(cn));
            }
        
            tasks.RemoveAll(t => t.IsCompleted);
            
            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, sleepDuration);
            await SleepAsync(sleepDuration, stoppingToken);
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
        
        return nextRunTime - now;
    }
    
    //
}
