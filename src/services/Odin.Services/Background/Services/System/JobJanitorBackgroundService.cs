using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;

namespace Odin.Services.Background.Services.System;

public class JobJanitorBackgroundService(
    ILogger<JobJanitorBackgroundService> logger)
    : AbstractBackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("JobJanitorBackgroundService is running");
            
            // SEB:TODO
            // Delete expired jobs

            await SleepAsync(TimeSpan.FromHours(1), stoppingToken);
        }
    }
    
}