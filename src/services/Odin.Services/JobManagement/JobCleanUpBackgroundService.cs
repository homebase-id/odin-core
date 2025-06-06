using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Background.BackgroundServices;
using Odin.Services.Configuration;

namespace Odin.Services.JobManagement;

public class JobCleanUpBackgroundService(
    ILogger<JobCleanUpBackgroundService> logger,
    OdinConfiguration config,
    IJobManager jobManager)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(config.BackgroundServices.JobCleanUpIntervalSeconds);
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);
            
            await jobManager.DeleteExpiredJobsAsync();
            
            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, interval);
            await SleepAsync(interval, stoppingToken);
        }
    }
}