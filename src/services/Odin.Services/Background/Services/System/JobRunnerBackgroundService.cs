using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;

namespace Odin.Services.Background.Services.System;

public class JobRunnerBackgroundService(
    ILogger<JobRunnerBackgroundService> logger,
    ServerSystemStorage serverSystemStorage)
    : AbstractBackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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