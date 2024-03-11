using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Util;
using Odin.Services.Configuration;

namespace Odin.Services.Drives.DriveCore.Storage;

/// <summary>
/// Handles read/write access to drive files to ensure correct
/// locking as well as apply system config for how files are written.
/// </summary>
public sealed class DriveFileReaderWriter(OdinConfiguration odinConfiguration, ConcurrentFileManager concurrentFileManager)
{
    public async Task WriteString(string filePath, string data)
    {
        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.FileOperationRetryAttempts,
            TimeSpan.FromMilliseconds(odinConfiguration.Host.FileOperationRetryDelayMs),
            CancellationToken.None,
            async () => await concurrentFileManager.WriteFile(filePath, path => File.WriteAllText(path, data)));
    }

    public async Task WriteAllBytes(string filePath, byte[] bytes)
    {
        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.FileOperationRetryAttempts,
            TimeSpan.FromMilliseconds(odinConfiguration.Host.FileOperationRetryDelayMs),
            CancellationToken.None,
            async () => await concurrentFileManager.WriteFile(filePath, path => File.WriteAllBytes(path, bytes)));
    }

    public async Task<uint> WriteStream(string filePath, Stream stream)
    {
        uint bytesWritten = 0;

        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.FileOperationRetryAttempts,
            TimeSpan.FromMilliseconds(odinConfiguration.Host.FileOperationRetryDelayMs),
            CancellationToken.None,
            async () =>
                await concurrentFileManager.WriteFile(filePath,
                    path => WriteStreamInternal(path, stream, odinConfiguration.Host.FileWriteChunkSizeInBytes, out bytesWritten))
        );

        if (bytesWritten != stream.Length)
        {
            throw new OdinSystemException($"Failed to write all expected data in stream. Wrote {bytesWritten} but should have been {stream.Length}");
        }

        return bytesWritten;
    }

    public async Task<byte[]> GetAllFileBytes(string filePath)
    {
        byte[] bytes = null;

        try
        {
            await TryRetry.WithDelayAsync(
                odinConfiguration.Host.FileOperationRetryAttempts,
                TimeSpan.FromMilliseconds(odinConfiguration.Host.FileOperationRetryDelayMs),
                CancellationToken.None,
                () => concurrentFileManager.ReadFile(filePath, path => bytes = File.ReadAllBytes(path)));
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

    public async Task MoveFile(string sourceFilePath, string destinationFilePath)
    {
        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.FileOperationRetryAttempts,
            TimeSpan.FromMilliseconds(odinConfiguration.Host.FileOperationRetryDelayMs),
            CancellationToken.None,
            async () => await concurrentFileManager.MoveFile(sourceFilePath, destinationFilePath, (s, d) => File.Move(s, d, true))
        );
    }

    /// <summary>
    /// Opens a filestream.  You must remember to close it.  Always opens in Read mode.
    /// </summary>
    public async Task<Stream> OpenStreamForReading(string filePath)
    {
        Stream fileStream = Stream.Null;
        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.FileOperationRetryAttempts,
            TimeSpan.FromMilliseconds(odinConfiguration.Host.FileOperationRetryDelayMs),
            CancellationToken.None,
            async () =>
                // MS: The CFM opens in ReadOnly mode. 
                fileStream = await concurrentFileManager.ReadStream(filePath)
        );

        return fileStream;
    }

    private static void WriteStreamInternal(string filePath, Stream stream, int chunkSize, out uint bytesWritten)
    {
        var buffer = new byte[chunkSize];

        using (var output = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            int bytesRead;
            do
            {
                bytesRead = stream.Read(buffer, 0, buffer.Length);
                output.Write(buffer, 0, bytesRead);
            } while (bytesRead > 0);

            bytesWritten = (uint)output.Length;
            output.Close();
        }
    }

    public async Task DeleteFile(string path)
    {
        //TODO: Consider if we need to do file.exists before deleting?
        await TryRetry.WithDelayAsync(
            odinConfiguration.Host.FileOperationRetryAttempts,
            TimeSpan.FromMilliseconds(odinConfiguration.Host.FileOperationRetryDelayMs),
            CancellationToken.None,
            async () =>
                await concurrentFileManager.DeleteFile(path)
        );
    }

    public async Task DeleteFiles(string[] paths)
    {
        foreach (var path in paths)
        {
            await this.DeleteFile(path);
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

    public async Task DeleteFilesInDirectory(string dir, string searchPattern)
    {
        if (Directory.Exists(dir))
        {
            var files = Directory.GetFiles(dir, searchPattern);
            await this.DeleteFiles(files);
        }
    }

    public Task<string[]> GetFilesInDirectory(string dir, string searchPattern = "*")
    {
        return Task.FromResult(Directory.GetFiles(dir!, searchPattern));
    }

    public Task CreateDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        return Task.CompletedTask;
    }
}