using System;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Util;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class PayloadS3ReaderWriter(ILogger<PayloadS3ReaderWriter> logger, IS3PayloadStorage s3PayloadsStorage) : IPayloadReaderWriter
{
    public async Task WriteFileAsync(string filePath, byte[] bytes, CancellationToken cancellationToken = default)
    {
        try
        {
            await TryRetry(async () =>
                await s3PayloadsStorage.WriteBytesAsync(filePath, bytes, cancellationToken), cancellationToken);
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
            await TryRetry(async () =>
                await s3PayloadsStorage.DeleteFileAsync(filePath, cancellationToken), cancellationToken);
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
            return await TryRetry(async () =>
                await s3PayloadsStorage.FileLengthAsync(filePath, cancellationToken), cancellationToken);
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
            return await TryRetry(async () =>
                await s3PayloadsStorage.FileExistsAsync(filePath, cancellationToken), cancellationToken);
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
            await TryRetry(async () =>
                await s3PayloadsStorage.MoveFileAsync(srcFilePath, dstFilePath, cancellationToken), cancellationToken);
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
            await TryRetry(async () =>
                await s3PayloadsStorage.UploadFileAsync(srcFilePath, dstFilePath, cancellationToken), cancellationToken);
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
            return await TryRetry(async () =>
                await s3PayloadsStorage.ReadBytesAsync(filePath, cancellationToken), cancellationToken);
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
            return await TryRetry(async () =>
                await s3PayloadsStorage.ReadBytesAsync(filePath, start, length, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new PayloadReaderWriterException(e.Message, e);
        }
    }

    //

    private RetryBuilder CreateRetry(CancellationToken cancellationToken)
    {
        return Core.Util.TryRetry.Create()
            .WithAttempts(5)
            .WithExponentialBackoff(TimeSpan.FromSeconds(5))
            .WithCancellation(cancellationToken)
            .WithLogging(logger)
            .WithoutWrapper()
            .RetryOnPredicate((ex, _) =>
            {
                if (ex.InnerException is not AmazonS3Exception s3Ex)
                {
                    return false;
                }

                // Retry on timeout
                if (s3Ex.Message.Contains("did not respond in time"))
                {
                    return true;
                }

                // Retry on http status code 5xx (outer)
                if ((int)s3Ex.StatusCode >= 500)
                {
                    return true;
                }

                // Retry on http status code 5xx (inner)
                if (s3Ex.InnerException is Amazon.Runtime.Internal.HttpErrorResponseException httpException)
                {
                    if ((int)httpException.Response.StatusCode >= 500)
                    {
                        return true;
                    }
                }

                // Don't retry 4xx client errors (NotFound, AccessDenied, etc.)
                return false;
            });
    }

    private Task<T> TryRetry<T>(Func<Task<T>> operation, CancellationToken ct)
        => CreateRetry(ct).ExecuteAsync(operation);

    private Task TryRetry(Func<Task> operation, CancellationToken ct)
        => CreateRetry(ct).ExecuteAsync(operation);

}
