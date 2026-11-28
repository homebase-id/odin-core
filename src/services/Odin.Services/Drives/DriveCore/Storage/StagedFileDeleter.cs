using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Deletes staged upload files on a thread of its own, so a request doesn't wait for the disk to remove them (#2221).
/// Staged files are always on local disk, and each staged path belongs to one write only.
/// A file that can't be deleted is skipped. Files still queued when the host stops stay behind, and the temp-folder
/// clean-up removes them after the next start.
/// </summary>
public sealed class StagedFileDeleter : IDisposable
{
    private readonly BlockingCollection<string> _queue = new();
    private readonly ILogger<StagedFileDeleter> _logger;

    public StagedFileDeleter(ILogger<StagedFileDeleter> logger)
    {
        _logger = logger;
        new Thread(DeleteQueued) { IsBackground = true, Name = nameof(StagedFileDeleter) }.Start();
    }

    public void Enqueue(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            try
            {
                _queue.Add(path);
            }
            catch (InvalidOperationException) when (_queue.IsAddingCompleted)
            {
                // The host is stopping: the temp-folder clean-up removes these after the next start
                return;
            }
        }
    }

    private void DeleteQueued()
    {
        foreach (var path in _queue.GetConsumingEnumerable())
        {
            try
            {
                File.Delete(path);
            }
            catch (DirectoryNotFoundException)
            {
                // Its folder is gone already (a drive purge removes the drive's whole upload folder)
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Couldn't delete staged file {path}; the temp-folder clean-up removes it later", path);
            }
        }
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
    }
}
