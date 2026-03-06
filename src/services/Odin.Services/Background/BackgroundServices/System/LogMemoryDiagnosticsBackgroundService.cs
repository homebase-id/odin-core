using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Logging.Memory;

namespace Odin.Services.Background.BackgroundServices.System;

public sealed class LogMemoryDiagnosticsBackgroundService(
    ILogger<LogMemoryDiagnosticsBackgroundService> logger,
    MemoryDiagnostics memoryDiagnostics):
    AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SleepAsync(TimeSpan.FromMinutes(10), stoppingToken);
            if (!stoppingToken.IsCancellationRequested)
            {
                memoryDiagnostics.LogMemoryBreakdown();
            }
        }
    }
}

