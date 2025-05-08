using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Intrinsics.X86;
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
    public async Task WriteStringAsync(string filePath, string data)
    {
        try
        {
            await TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .ExecuteAsync(async () =>
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
        try
        {
            await TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .ExecuteAsync(async () =>
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

    public async Task<uint> WriteStreamAsync(string filePath, Stream stream, bool byPassInternalFileLocking = false)
    {
        uint bytesWritten = 0;

        try
        {
            await TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .ExecuteAsync(async () =>
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

    public async Task<byte[]> GetAllFileBytesAsync(string filePath, bool byPassInternalFileLocking = false)
    {
        byte[] bytes = null;

        try
        {
            await TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .ExecuteAsync(async () =>
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
        try
        {
            TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .Execute(() =>
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
        
        if (File.Exists(sourceFilePath))
        {
            DeleteFile(sourceFilePath);
            
            if (File.Exists(sourceFilePath))
            {
                throw new OdinSystemException(
                    $"Error during file move operation.  FileMove reported success but source file is still on disk [source file: {sourceFilePath}] [destination: {destinationFilePath}]");
            }
        }
    }

    //public void CopyFile(string sourceFilePath, string destinationFilePath)
    //{
    //    try
    //    {
    //        TryRetry.Create()
    //            .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
    //            .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
    //            .Execute(() =>
    //            {
    //                try
    //                {
    //                    File.Copy(sourceFilePath, destinationFilePath, true);
    //                }
    //                catch (Exception e)
    //                {
    //                    logger.LogDebug(e, "MoveFile (TryRetry) {message}", e.Message);
    //                    throw;
    //                }
    //            });
    //    }
    //    catch (TryRetryException e)
    //    {
    //        throw e.InnerException!;
    //    }

    //    if (!File.Exists(destinationFilePath))
    //    {
    //        throw new OdinSystemException(
    //            $"Error during file copy operation.  FileMove reported success but destination file does not exist. [source file: {sourceFilePath}] [destination: {destinationFilePath}]");
    //    }
    //}

    public void CopyFileSafely(string sourcePath, string targetPath)
    {
        // Ensure the source file exists
        FileInfo sourceInfo = new FileInfo(sourcePath);
        if (!sourceInfo.Exists)
        {
            throw new OdinSystemException($"Source file '{sourcePath}' does not exist.");   
        }
        
        // Check if the target file exists. If it does it is either already live
        // due to a bug, or a previous copy attempt copied some files but not all
        // let's not copy them again, the disk/network is likely slow then
        FileInfo targetInfo = new FileInfo(targetPath);
        if (targetInfo.Exists)
        {        
            // If sizes match, assume it’s valid and skip the copy
            if (targetInfo.Length == sourceInfo.Length)
            {
                logger.LogDebug($"CopyFile: Target file '{targetPath}' already exists and is valid. No action needed.");
                return;
            }
            else
            {
                // If sizes don’t match, it’s corrupt—delete it
                logger.LogDebug($"CopyFile: Target file '{targetPath}' is corrupt. Deleting it.");
                File.Delete(targetPath);        
            }    
        }

        try
        {
            TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .Execute(() =>
            {
                try
                {
                    File.Copy(sourcePath, targetPath, overwrite: false);
                }
                catch (IOException ex) when (ex.Message.Contains("already exists"))
                {
                    // If the copy fails because the file now exists, assume a competing thread created it
                    throw new OdinSystemException($"Target file '{targetPath}' was created by another thread during the operation.");
                }
                catch (Exception ex)
                {
                    // Handle other potential errors (e.g., permissions or disk issues)
                    logger.LogDebug(ex, "CopyFile (TryRetry) {message}", ex.Message);
                    throw new OdinSystemException($"Failed to copy file: {ex.Message}", ex);
                }
            });

            // This seems unnecessary
            targetInfo = new FileInfo(targetPath);
            if (targetInfo.Exists)
            {
                // If sizes match, assume it’s valid and skip the copy
                if (targetInfo.Length == sourceInfo.Length)
                {
                    return;
                }
            }
            throw new OdinSystemException(
                $"Error during file copy operation.  FileCopy reported success but destination file does not exist or is incorrect size. [source file: {sourcePath}] [destination: {targetPath}]");
        }
        catch (TryRetryException e)
        {
            throw e.InnerException!;
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
            TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .Execute(() =>
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
            TryRetry.Create()
                .WithAttempts(odinConfiguration.Host.FileOperationRetryAttempts)
                .WithDelay(odinConfiguration.Host.FileOperationRetryDelayMs)
                .Execute(() =>
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
    }

    public void DeleteFiles(IEnumerable<string> paths)
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

    public void CreateDirectory(string dir)
    {
        Directory.CreateDirectory(dir);
        logger.LogDebug("Created Directory [{dir}]", dir);
    }
}