using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Configuration;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Handles read/write access to drive files to ensure correct
/// locking as well as apply system config for how files are written.
/// </summary>
public sealed class DriveFileReaderWriter(
    OdinConfiguration odinConfiguration,
    ILogger<DriveFileReaderWriter> logger)
{
    public async Task WriteString(string filePath, string data)
    {
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

    public async Task WriteAllBytes(string filePath, byte[] bytes)
    {
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

    public async Task<uint> WriteStream(string filePath, Stream stream, bool byPassInternalFileLocking = false)
    {
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

    public async Task<byte[]> GetAllFileBytes(string filePath, bool byPassInternalFileLocking = false)
    {
        byte[] bytes = null;

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

    public Task MoveFile(string sourceFilePath, string destinationFilePath)
    {
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

        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens a filestream.  You must remember to close it.  Always opens in Read mode.
    /// </summary>
    public Task<Stream> OpenStreamForReading(string filePath)
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

        return Task.FromResult(fileStream);
    }

    private async Task<uint> WriteStreamInternalAsync(string filePath, Stream stream)
    {
        int chunkSize = odinConfiguration.Host.FileWriteChunkSizeInBytes;
        var buffer = new byte[chunkSize];

        uint bytesWritten;
        using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int bytesRead;
            do
            {
                bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                await output.WriteAsync(buffer, 0, bytesRead);
            } while (bytesRead > 0);

            bytesWritten = (uint)output.Length;
            output.Close();
        }

        return bytesWritten;
    }

    public Task DeleteFileAsync(string path)
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
                        logger.LogDebug(e, "DeleteFileAsync (TryRetry) {message}", e.Message);
                        throw;
                    }
                });
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
        }

        return Task.CompletedTask;
    }

    public async Task DeleteFilesAsync(string[] paths)
    {
        foreach (var path in paths)
        {
            await DeleteFileAsync(path);
        }
    }

    public Task<bool> FileExists(string filePath)
    {
        return Task.FromResult(File.Exists(filePath));
    }

    public Task<bool> DirectoryExists(string dir)
    {
        return Task.FromResult(Directory.Exists(dir));
    }

    public async Task DeleteFilesInDirectoryAsync(string dir, string searchPattern)
    {
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, searchPattern);
            await DeleteFilesAsync(files);
        }
    }

    public string[] GetFilesInDirectory(string dir, string searchPattern = "*")
    {
        return Directory.GetFiles(dir!, searchPattern);
    }

    public void CreateDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        logger.LogDebug("Created Directory [{dir}]", dir);
    }
}