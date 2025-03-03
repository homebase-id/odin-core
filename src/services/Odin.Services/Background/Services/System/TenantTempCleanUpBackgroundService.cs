using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Configuration;
using Odin.Services.Registry;

namespace Odin.Services.Background.Services.System;

public sealed class TenantTempCleanUpBackgroundService(
    ILogger<TenantTempCleanUpBackgroundService> logger,
    OdinConfiguration config)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var deleteFilesOlderThan = TimeSpan.FromHours(24);
        var interval = TimeSpan.FromHours(1);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);

            try
            {
                var tempPath = Path.Combine(config.Host.TenantDataRootPath, FileSystemIdentityRegistry.TempPath);
                TenantTempCleanUp.Execute(logger, tempPath, deleteFilesOlderThan, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in TenantTempCleanUpBackgroundService: {message}", ex.Message);
            }

            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, interval);
            await SleepAsync(interval, stoppingToken);
        }
    }
}

//

public static class TenantTempCleanUp
{
    public static void Execute(
        ILogger logger,
        string tempPath,
        TimeSpan deleteFilesOlderThan,
        CancellationToken stoppingToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempPath);

        if (!Directory.Exists(tempPath))
        {
            logger.LogDebug("Temp path {tempPath} does not exist (yet?)", tempPath);
            return;
        }

        var now = DateTime.Now;
        var threshold = now - deleteFilesOlderThan;

        var tempFiles = Directory.GetFiles(tempPath);
        foreach (var file in tempFiles)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            var creationTime = File.GetCreationTime(file);
            if (creationTime < threshold)
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to delete temp file {file}", file);
                }
            }
        }
    }
}