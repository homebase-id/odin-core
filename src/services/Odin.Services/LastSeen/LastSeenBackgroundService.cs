using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.LastSeen;

public sealed class LastSeenBackgroundService(
    ILogger<LastSeenBackgroundService> logger,
    ILastSeenService lastSeenService)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var saveInterval = TimeSpan.FromMinutes(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SleepAsync(saveInterval, stoppingToken);
            await SaveAsync();
            if (!stoppingToken.IsCancellationRequested)
            {
                await DeleteOldDatabaseRecords();
            }
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

    //

    private async Task DeleteOldDatabaseRecords()
    {
        try
        {
            logger.LogDebug("Deleting old last seen identities");
            var impl = (LastSeenService)lastSeenService;
            await impl.DeleteOldDatabaseRecords();
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error deleting old seen identities");
        }
    }

}

