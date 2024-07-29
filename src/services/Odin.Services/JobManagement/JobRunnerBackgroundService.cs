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
    ServerSystemStorage serverSystemStorage) : AbstractBackgroundService
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
            logger.LogDebug("JobRunnerBackgroundService is running");

            TimeSpan? nextRun;
            using (var cn = serverSystemStorage.CreateConnection())
            {
                while (!stoppingToken.IsCancellationRequested && await jobs.GetNextScheduledJob(cn) is { } job)
                {
                    var task = jobManager.RunJobNowAsync(job.id, stoppingToken);
                    tasks.Add(task);
                }
            
                var ts = await jobs.GetNextRunTime(cn);
                nextRun = ts.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(ts.Value) - DateTimeOffset.Now : null;
            }
        
            tasks.RemoveAll(t => t.IsCompleted);
            
            await SleepAsync(nextRun, stoppingToken);
        }
        await Task.WhenAll(tasks);
    }
}
