using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.Registry.LastSeen;

public sealed class LastSeenBackgroundService(
    ILogger<LastSeenBackgroundService> logger,
    ILastSeenService lastSeenService)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var saveInterval = TimeSpan.FromMinutes(10);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await SleepAsync(saveInterval, stoppingToken);
                await SaveAsync();
            }
        }
        finally
        {
            // Make sure we save on shutdown
            await SaveAsync();
        }
    }

    //

    private async Task SaveAsync()
    {
        try
        {
            logger.LogDebug("Saving last seen identities");
            var impl = (LastSeenService)lastSeenService;
            await impl.UpdateDatabaseAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error saving last seen identities");
        }
    }
}

