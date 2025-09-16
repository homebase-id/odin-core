using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database;

namespace Odin.Services.Background.BackgroundServices.System;

public sealed class LogTransactionalCacheStatsBackgroundService(
    ILogger<LogTransactionalCacheStatsBackgroundService> logger,
    ITransactionalCacheStats cacheStats)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await SleepAsync(TimeSpan.FromHours(1), stoppingToken);
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            var stats = cacheStats.GetAllStats().OrderBy(kvp => kvp.Key);
            foreach (var kvp in stats)
            {
                var total = kvp.Value.Hits + kvp.Value.Misses;
                var hitRate = total == 0 ? 0.0 : (double)kvp.Value.Hits / total * 100;
                logger.LogInformation("DB cache stats: {key}: hits={hits}, misses={misses}, hit rate={hitRate:F1}%",
                    kvp.Key,
                    kvp.Value.Hits,
                    kvp.Value.Misses,
                    hitRate);
            }
        }
    }
}

