using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Background.Services;
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
        var interval = config.Job.JobCleanUpIntervalSeconds;
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("JobCleanUpBackgroundService is running");
            await jobManager.DeleteExpiredJobsAsync();
            await SleepAsync(TimeSpan.FromSeconds(interval), stoppingToken);
        }
    }
}