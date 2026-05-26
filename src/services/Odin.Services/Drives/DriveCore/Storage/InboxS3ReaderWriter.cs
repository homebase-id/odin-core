using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Util;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class InboxS3ReaderWriter(ILogger<InboxS3ReaderWriter> logger, IS3InboxStorage s3InboxStorage) : IInboxReaderWriter
{
    public async Task<uint> WriteStreamAsync(string filePath, Stream stream, CancellationToken cancellationToken = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3InboxStorage.WriteStreamAsync(filePath, stream, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public async Task<byte[]> GetFileBytesAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3InboxStorage.ReadBytesAsync(filePath, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3InboxStorage.FileExistsAsync(filePath, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    public Task EnsureDirectoryAsync(string dir, CancellationToken cancellationToken = default)
    {
        // No-op: S3 has no directories.
        return Task.CompletedTask;
    }

    public async Task DeleteByPrefixAsync(string pathPrefix, CancellationToken cancellationToken = default)
    {
        try
        {
            await TryRetry(async () =>
                await s3InboxStorage.DeleteByPrefixAsync(pathPrefix, cancellationToken), cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new InboxReaderWriterException(e.Message, e);
        }
    }

    //
    // Copied verbatim from PayloadS3ReaderWriter.cs
    //

    private RetryBuilder CreateRetry(CancellationToken cancellationToken)
    {
        return Core.Util.TryRetry.Create()
            .WithAttempts(5)
            .WithExponentialBackoff(TimeSpan.FromSeconds(5))
            .WithCancellation(cancellationToken)
            .WithLogging(logger)
            .WithoutExceptionWrapper()
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
