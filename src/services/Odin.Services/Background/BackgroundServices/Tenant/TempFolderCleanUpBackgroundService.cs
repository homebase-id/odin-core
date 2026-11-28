using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;
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

        // Delay initial run with some random time, so we don't all run at the same time, doing lots of synchronous IO
        await SleepAsync(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(120), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogDebug("{service} is running", GetType().Name);

            try
            {
                UploadFolderCleanUp.Execute(
                    logger,
                    tenantContext.TenantPathManager.UploadDrivesPath,
                    UploadFolderCleanUp.Cutoff(DateTime.UtcNow, UploadFolderCleanUp.ProcessStartedUtc, uploadAgeThreshold),
                    stoppingToken);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "UploadFolderCleanUp: {message}", e.Message);
            }

            var interval = TimeSpan.FromHours(24);
            logger.LogDebug("{service} is sleeping for {SleepDuration}", GetType().Name, interval);
            await SleepAsync(interval, stoppingToken);
        }
    }
}

//

public static class UploadFolderCleanUp
{
    public static readonly DateTime ProcessStartedUtc = Process.GetCurrentProcess().StartTime.ToUniversalTime();

    /// <summary>
    /// Files last written before this are removed: those older than <paramref name="ageThreshold"/>, and anything
    /// staged before the process started. The upload folder is local to this host, so no request still running can
    /// own a file from before the start (#2221: deletes still queued at shutdown are left behind).
    /// </summary>
    public static DateTime Cutoff(DateTime nowUtc, DateTime processStartedUtc, TimeSpan ageThreshold)
    {
        var byAge = nowUtc - ageThreshold;
        return byAge > processStartedUtc ? byAge : processStartedUtc;
    }

    public static void Execute(
        ILogger logger,
        string uploadDrivesPath,
        DateTime cutoffUtc,
        CancellationToken stoppingToken = default)
    {
        if (!Directory.Exists(uploadDrivesPath))
        {
            return;
        }

        var drives = Directory.GetDirectories(uploadDrivesPath);
        foreach (var drive in drives)
        {
            stoppingToken.ThrowIfCancellationRequested();

            var uploadsPath = Path.Combine(drive, TenantPathManager.UploadFolder);
            CleanUp(logger, uploadsPath, cutoffUtc, stoppingToken);
        }
    }

    //

    private static void CleanUp(ILogger logger, string folder, DateTime cutoffTime, CancellationToken stoppingToken)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        logger.LogDebug("{service} is scanning {folder}", nameof(UploadFolderCleanUp), folder);

        try
        {
            var subDirectories = Directory.GetDirectories(folder);
            if (subDirectories.Length > 0)
            {
                logger.LogError("Illegal subdirectories detected in {Folder}", folder);
            }

            var files = Directory.GetFiles(folder);

            foreach (var file in files)
            {
                stoppingToken.ThrowIfCancellationRequested();

                try
                {
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.LastWriteTimeUtc < cutoffTime)
                    {
                        logger.LogDebug("UploadFolderCleanUp: deleting file {file}", file);
                        File.Delete(file);
                    }
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogError(e, "UploadFolderCleanUp({file}): {message}", file, e.Message);
                }
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "UploadFolderCleanUp({folder}): {message}", folder, e.Message);
        }
    }
}



