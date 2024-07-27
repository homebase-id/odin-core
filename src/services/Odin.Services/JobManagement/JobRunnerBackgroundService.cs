using System;
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
        
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("JobRunnerBackgroundService is running");

            using (var cn = serverSystemStorage.CreateConnection())
            {
                // SEB:TODO

            }
            
            await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}