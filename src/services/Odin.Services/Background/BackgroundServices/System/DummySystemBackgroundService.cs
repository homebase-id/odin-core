using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Odin.Services.Background.BackgroundServices.System;

public sealed class DummySystemBackgroundService(ILogger<DummySystemBackgroundService> logger)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("DummySystemBackgroundService is running");
            await SleepAsync(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}

