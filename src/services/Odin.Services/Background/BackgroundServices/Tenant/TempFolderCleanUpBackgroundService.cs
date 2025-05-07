using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Directory = System.IO.Directory;

namespace Odin.Services.Background.BackgroundServices.Tenant;

public sealed class TempFolderCleanUpBackgroundService(
    ILogger<TempFolderCleanUpBackgroundService> logger,
    TenantContext tenantContext)
    : AbstractBackgroundService(logger)
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var uploadAgeThreshold = TimeSpan.FromHours(24);
        var inboxAgeThreshold = TimeSpan.FromDays(365 * 100); // NOTE: we currently never expire inbox files

        // Delay initial run with some random time, so we don't all run at the same time, doing lots of synchronous IO
        await SleepAsync(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(120), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);

            try
            {
                TempFolderCleanUp.Execute(
                    logger,
                    tenantContext.TenantPathManager.TempStoragePath,
                    uploadAgeThreshold,
                    inboxAgeThreshold,
                    stoppingToken);
            }
            catch (Exception e)
            {
                logger.LogError(e, "TempFolderCleanUpBackgroundService: {message}", e.Message);
            }

            var interval = TimeSpan.FromHours(24);
            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, interval);
            await SleepAsync(interval, stoppingToken);
        }
    }
}

//

public static class TempFolderCleanUp
{
    public static void Execute(
        ILogger logger,
        string tempFolder,
        TimeSpan uploadAgeThreshold,
        TimeSpan inboxAgeThreshold,
        CancellationToken stoppingToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tempFolder, nameof(tempFolder));

        if (uploadAgeThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentException("Upload age threshold must be positive", nameof(uploadAgeThreshold));
        }

        if (inboxAgeThreshold <= TimeSpan.Zero)
        {
            throw new ArgumentException("Inbox age threshold must be positive", nameof(inboxAgeThreshold));
        }

        if (!Directory.Exists(tempFolder))
        {
            throw new OdinSystemException($"Temp folder {tempFolder} does not exist");
        }

        var drivesFolder = Path.Combine(tempFolder, "drives"); // SEB:TODO get this path from PathManager
        if (!Directory.Exists(drivesFolder))
        {
            return;
        }

        var drives = Directory.GetDirectories(drivesFolder);
        foreach (var drive in drives)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                return;
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                var uploadsPath = Path.Combine(drive, "uploads"); // SEB:TODO get this path from PathManager
                CleanUp(logger, uploadsPath, uploadAgeThreshold, stoppingToken);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                var inboxPath = Path.Combine(drive, "inbox"); // SEB:TODO get this path from PathManager
                CleanUp(logger, inboxPath, inboxAgeThreshold, stoppingToken);
            }
        }
    }

    //

    private static void CleanUp(ILogger logger, string folder, TimeSpan threshold, CancellationToken stoppingToken)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        logger.LogDebug("{service} is scanning {folder}", nameof(TempFolderCleanUp), folder);

        try
        {
            var subDirectories = Directory.GetDirectories(folder);
            if (subDirectories.Length > 0)
            {
                logger.LogError("Illegal subdirectories detected in {Folder}", folder);
            }

            var cutoffTime = DateTime.UtcNow.Subtract(threshold);
            var files = Directory.GetFiles(folder);

            foreach (var file in files)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoffTime)
                    {
                        logger.LogDebug("TempFolderCleanUp: deleting file {file}", file);
                        File.Delete(file);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, "TempFolderCleanUp({file}): {message}", file, e.Message);
                }
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "TempFolderCleanUp({folder}): {message}", folder, e.Message);
        }
    }
}

