using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.System.Table;
using Odin.Services.Background.BackgroundServices;

namespace Odin.Services.Registry.LastSeen;

public sealed class LastSeenBackgroundService(
    ILogger<LastSeenBackgroundService> logger,
    ILastSeenService lastSeenService,
    TableRegistrations tableRegistrations)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var saveInterval = TimeSpan.FromMinutes(10);

        await LoadAsync();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await SaveAsync(); // Save right away => fail fast!
                await SleepAsync(saveInterval, stoppingToken);
            }
        }
        finally
        {
            // Make sure we save on shutdown
            await SaveAsync();
        }
    }

    //

    private async Task LoadAsync()
    {
        try
        {
            logger.LogDebug("Loading last seen identities");
            var records = await tableRegistrations.GetAllAsync();
            foreach (var record in records)
            {
                // lastSeenService.PutLastSeen(record.identityId, ); // SEB:TODO
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while loading last seen identities");
        }
    }

    //

    private async Task SaveAsync()
    {
        try
        {
            logger.LogDebug("Saving last seen identities");
            var lastSeen = lastSeenService.All;
            foreach (var record in lastSeen)
            {
                // tableRegistrations.UpsertLastSeen(record.Key, record.Value); // SEB:TODO
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "Error while saving last seen identities");
        }
    }
}

