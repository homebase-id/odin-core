using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class PayloadFileReaderWriter(
    ILogger<PayloadFileReaderWriter> logger,
    TenantContext tenantContext,
    FileReaderWriter fileReaderWriter
) : IPayloadReaderWriter
{
    private readonly ILogger<PayloadFileReaderWriter> _logger = logger;
    private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

    //

    public async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var directory = Path.GetDirectoryName(filePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            await fileReaderWriter.WriteAllBytesAsync(filePath, bytes, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            fileReaderWriter.DeleteFile(filePath);
            return Task.CompletedTask;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //
    public Task<long> FileLengthAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var fi = new FileInfo(filePath);
            return Task.FromResult(fi.Length);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return Task.FromResult(fileReaderWriter.FileExists(filePath));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task MoveFileAsync(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!File.Exists(srcFilePath))
            {
                throw new FileNotFoundException(srcFilePath);
            }

            var dstDirectory = Path.GetDirectoryName(dstFilePath);
            if (dstDirectory != null && !Directory.Exists(dstDirectory))
            {
                Directory.CreateDirectory(dstDirectory);
            }

            fileReaderWriter.MoveFile(srcFilePath, dstFilePath);
            return Task.CompletedTask;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            fileReaderWriter.CreateDirectory(dir);
            return Task.CompletedTask;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task CopyPayloadFileAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            fileReaderWriter.CopyPayloadFile(sourcePath, targetPath);
            return Task.CompletedTask;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task<byte[]> GetFileBytesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return GetFileBytesAsync(filePath, 0, long.MaxValue, cancellationToken);
    }

    //

    public async Task<byte[]> GetFileBytesAsync(
        string filePath,
        long start,
        long length,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return await fileReaderWriter.GetFileBytesAsync(filePath, start, length, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

}