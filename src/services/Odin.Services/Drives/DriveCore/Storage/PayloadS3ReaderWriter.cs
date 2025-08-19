using System;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Storage.ObjectStorage;
using Odin.Services.Base;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class PayloadS3ReaderWriter(
    TenantContext tenantContext,
    IS3PayloadStorage s3PayloadsStorage) : IPayloadReaderWriter
{
    private readonly TenantPathManager _tenantPathManager = tenantContext.TenantPathManager;

    //

    public async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3PayloadsStorage.WriteBytesAsync(filePath, bytes, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task DeleteFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3PayloadsStorage.DeleteFileAsync(filePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task<long> FileLengthAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await s3PayloadsStorage.FileLengthAsync(filePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await s3PayloadsStorage.FileExistsAsync(filePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task MoveFileAsync(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3PayloadsStorage.MoveFileAsync(srcFilePath, dstFilePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public Task CreateDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        // No-op: S3 does not have directories in the same way as a file system.
        return Task.CompletedTask;
    }

    //

    public async Task CopyPayloadFileAsync(string srcFilePath, string dstFilePath, CancellationToken cancellationToken = default)
    {
        try
        {
            await s3PayloadsStorage.UploadFileAsync(srcFilePath, dstFilePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task<byte[]> GetFileBytesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await s3PayloadsStorage.ReadBytesAsync(filePath, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    public async Task<byte[]> GetFileBytesAsync(
        string filePath,
        long start,
        long length,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await s3PayloadsStorage.ReadBytesAsync(filePath, start, length, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

}
