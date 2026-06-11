using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Util;
using Odin.Services.Configuration;

namespace Odin.Services.Drives.DriveCore.Storage;

#nullable enable

public class S3FileStore(
    IS3Storage s3,
    ILogger<S3FileStore> logger,
    OdinConfiguration config)
    : IDriveFileStore
{
    private readonly int _retryAttempts = Math.Max(1, config.S3Storage.RetryAttempts);
    private readonly TimeSpan _retryInitialBackoff =
        TimeSpan.FromMilliseconds(Math.Max(0, config.S3Storage.RetryInitialBackoffMs));

    public StorageBackendType Backend => StorageBackendType.S3;

    //

    public async Task<uint> WriteStreamAsync(string path, Stream stream, CancellationToken ct = default)
    {
        try
        {
            var written = await TryRetry(async () =>
                await s3.WriteStreamAsync(path, stream, ct), ct);
            return (uint)written;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken ct = default)
    {
        try
        {
            await TryRetry(async () =>
                await s3.WriteBytesAsync(path, bytes, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3.ReadBytesAsync(path, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public async Task<byte[]> ReadBytesAsync(string path, long start, long length, CancellationToken ct = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3.ReadBytesAsync(path, start, length, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3.FileExistsAsync(path, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public async Task<long> LengthAsync(string path, CancellationToken ct = default)
    {
        try
        {
            return await TryRetry(async () =>
                await s3.FileLengthAsync(path, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public async Task DeleteAsync(string path, CancellationToken ct = default)
    {
        try
        {
            await TryRetry(async () =>
                await s3.DeleteFileAsync(path, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public async Task DeleteSetAsync(string dir, Guid fileId, CancellationToken ct = default)
    {
        var prefix = S3Path.Combine(dir, $"{fileId:N}.");
        try
        {
            await TryRetry(async () =>
                await s3.DeleteByPrefixAsync(prefix, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    //

    public Task EnsureDirectoryAsync(string dir, CancellationToken ct = default)
    {
        // No-op: S3 does not have directories.
        return Task.CompletedTask;
    }

    //

    /// <summary>
    /// Promotes a staged file into this S3 store. The destination is S3, so the source is either S3 (an
    /// S3 -&gt; S3 server-side copy, possibly across buckets) or disk (a Disk -&gt; S3 upload). Those are the
    /// only two cases under the all-or-nothing rule; there is no S3 -&gt; disk here because the destination is
    /// always S3. Both branches run under the shared retry policy and wrap non-cancellation failures as
    /// <see cref="DriveFileStoreException"/> (cancellation propagates unwrapped).
    /// </summary>
    public async Task IngestFromAsync(IDriveFileStore source, string sourcePath, string destPath, CancellationToken ct = default)
    {
        if (source.Backend == StorageBackendType.S3)
        {
            // S3 -> S3: copy server-side, WITHOUT round-tripping the bytes through this host. Inbox and
            // payload can live in different buckets, so we need the source's bucket and its full object key
            // (with the source store's root prefix already applied). GetS3Location supplies both. It only
            // returns null for a non-S3 store, which a Backend==S3 source can never be; the null guard keeps
            // that contract explicit rather than dereferencing blindly.
            var loc = source.GetS3Location(sourcePath)
                ?? throw new DriveFileStoreException("S3 dest can only ingest from an S3-backed source");
            try
            {
                await TryRetry(async () =>
                    await s3.CopyFromBucketAsync(loc.bucket, loc.fullKey, destPath, ct), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new DriveFileStoreException(e.Message, e);
            }
            return;
        }

        // Disk -> S3: the source is a genuine local file (always-disk upload staging). Read it and PUT it
        // into this S3 store. sourcePath is a local file system path; destPath is this store's relative key.
        try
        {
            await TryRetry(async () =>
                await s3.UploadFileAsync(sourcePath, destPath, ct), ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new DriveFileStoreException(e.Message, e);
        }
    }

    public (string bucket, string fullKey)? GetS3Location(string relativePath)
        => (s3.BucketName, s3.GetFullKey(relativePath));

    //

    private RetryBuilder CreateRetry(CancellationToken cancellationToken)
    {
        return Core.Util.TryRetry.Create()
            .WithAttempts(_retryAttempts)
            .WithExponentialBackoff(_retryInitialBackoff)
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
