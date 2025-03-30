using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Configuration;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

/// <summary>
/// Handles read/write access to drive files to ensure correct
/// locking as well as apply system config for how files are written.
/// </summary>
public sealed class DriveFileReaderWriter(
    OdinConfiguration odinConfiguration,
    ILogger<DriveFileReaderWriter> logger)
{
    public async Task WriteStringAsync(string filePath, string data)
    {
        CreateDirectory(Path.GetDirectoryName(filePath));
        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                async () =>
                {
                    try
                    {
                        await File.WriteAllTextAsync(filePath, data);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "WriteString (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }
    }

    public async Task WriteAllBytesAsync(string filePath, byte[] bytes)
    {
        CreateDirectory(Path.GetDirectoryName(filePath));
        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                async () =>
                {
                    try
                    {
                        CreateDirectory(Path.GetDirectoryName(filePath));
                        await File.WriteAllBytesAsync(filePath, bytes);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "WriteAllBytes (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }
    }

    public async Task<uint> WriteStreamAsync(string filePath, Stream stream)
    {
        CreateDirectory(Path.GetDirectoryName(filePath));
        uint bytesWritten = 0;

        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                async () =>
                {
                    try
                    {
                        bytesWritten = await WriteStreamInternalAsync(filePath, stream);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "WriteStream (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }

        if (bytesWritten != stream.Length)
        {
            throw new OdinSystemException(
                $"Failed to write all expected data in stream. Wrote {bytesWritten} but should have been {stream.Length}");
        }

        return bytesWritten;
    }

    public async Task<byte[]?> GetAllFileBytesAsync(string filePath, bool byPassInternalFileLocking = false)
    {
        CreateDirectory(Path.GetDirectoryName(filePath));
        byte[]? bytes = null;

        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                async () =>
                {
                    try
                    {
                        bytes = await File.ReadAllBytesAsync(filePath);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "GetAllFileBytes (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            if (e.InnerException is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }

            throw;
        }

        return bytes;
    }

    public void MoveFile(string sourceFilePath, string destinationFilePath)
    {
        CreateDirectory(Path.GetDirectoryName(destinationFilePath));
        try
        {
            TryRetry.WithDelay(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                () =>
                {
                    try
                    {
                        File.Move(sourceFilePath, destinationFilePath, true);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "MoveFile (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }

        if (!File.Exists(destinationFilePath))
        {
            throw new OdinSystemException(
                $"Error during file move operation.  FileMove reported success but destination file does not exist. [source file: {sourceFilePath}] [destination: {destinationFilePath}]");
        }
    }

    /// <summary>
    /// Opens a filestream.  You must remember to close it.  Always opens in Read mode.
    /// </summary>
    public Stream OpenStreamForReading(string filePath)
    {
        Stream fileStream = Stream.Null;

        try
        {
            TryRetry.WithDelay(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                () =>
                {
                    try
                    {
                        fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "OpenStreamForReading (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }

        return fileStream;
    }

    private async Task<uint> WriteStreamInternalAsync(string filePath, Stream stream)
    {
        CreateDirectory(Path.GetDirectoryName(filePath));
        
        var chunkSize = odinConfiguration.Host.FileWriteChunkSizeInBytes;
        var buffer = new byte[chunkSize];

        await using var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
        int bytesRead;
        do
        {
            bytesRead = await stream.ReadAsync(buffer);
            await output.WriteAsync(buffer.AsMemory(0, bytesRead));
        } while (bytesRead > 0);

        var bytesWritten = (uint)output.Length;

        return bytesWritten;
    }

    public void DeleteFile(string path)
    {
        try
        {
            //TODO: Consider if we need to do file.exists before deleting?
            TryRetry.WithDelay(
                odinConfiguration.Host.FileOperationRetryAttempts,
                odinConfiguration.Host.FileOperationRetryDelayMs,
                CancellationToken.None,
                () =>
                {
                    try
                    {
                        File.Delete(path);
                    }
                    catch (Exception e)
                    {
                        logger.LogDebug(e, "DeleteFile (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }
    }

    public void DeleteFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            DeleteFile(path);
        }
    }

    public bool FileExists(string filePath)
    {
        return File.Exists(filePath);
    }

    public bool DirectoryExists(string dir)
    {
        return Directory.Exists(dir);
    }

    public void DeleteFilesInDirectory(string dir, string searchPattern)
    {
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, searchPattern);
            DeleteFiles(files);
        }
    }

    public string[] GetFilesInDirectory(string dir, string searchPattern = "*")
    {
        return Directory.GetFiles(dir!, searchPattern);
    }

    public void CreateDirectory(string? dir)
    {
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            logger.LogDebug("Created Directory [{dir}]", dir);
        }
    }
}