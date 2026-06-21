using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

//
// SEB:NOTE
// There is no TryRetry here because S3 SDK already has retry logic built-in.
// It defaults to 3 retries with exponential backoff.
// Can be overridden by configuring the AmazonS3Config when creating the client.
//

public class S3AwsStorage : IS3Storage
{
    private readonly ILogger<S3AwsStorage> _logger;
    private readonly IAmazonS3 _s3Client;
    private readonly string _rootPath;

    // Multipart part size for TransferUtility file uploads (16 MB).
    // AWSSDK.S3 4.0.17's default part size is 5 MB, which inflates the part/request count for the
    // 5GB+ files this path targets (TODO #1). 16 MB = fewer requests while staying well under the
    // 10,000-part ceiling (16 MB * 10,000 ~= 160 GB before the cap bites) and each part is <= 5 GB.
    // Tune per provider / object-size profile.
    private const long UploadPartSizeBytes = 16L * 1024 * 1024;

    // Max concurrent part uploads for TransferUtility. Matches the SDK
    // default of 10. On a VPS that proxies the bytes, NIC/memory is the real ceiling; lower this if
    // concurrent uploads contend for bandwidth.
    private const int UploadConcurrencyLimit = 10;

    public string BucketName { get; }

    public S3AwsStorage(ILogger<S3AwsStorage> logger, IAmazonS3 s3Client, string bucketName, string rootPath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));
        ArgumentNullException.ThrowIfNull(rootPath, nameof(rootPath));

        _logger = logger;
        _s3Client = s3Client;
        BucketName = bucketName;
        _rootPath = S3Path.Combine(rootPath);
    }

    //

    public async Task CreateBucketAsync(CancellationToken cancellationToken = default)
    {
        if (!await BucketExistsAsync(cancellationToken))
        {
            try
            {
                await _s3Client.PutBucketAsync(BucketName, cancellationToken);
            }
            catch (Exception ex)
            {
                throw CreateS3StorageException(ex, $"Create bucket '{BucketName} failed'.");
            }
        }

        // Every bucket gets the abort-incomplete-multipart backstop: S3 reaps crash-orphaned upload
        // parts (process died before CompleteMultipartUpload, so no in-process abort ran). Reconciled
        // on every call -- not only first creation -- so pre-existing buckets pick it up on restart.
        // Best-effort: some providers reject this (Vultr has no lifecycle; MinIO refuses bucket-level
        // abort rules by design and purges stale uploads globally instead). They must NOT fail bucket
        // creation over a cost-hygiene rule, so log and continue. Cancellation still propagates.
        try
        {
            await EnsureAbortIncompleteMultipartLifecycleAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Log ex.Message (not ex) so this stays a single line: this is the expected outcome on
            // providers that reject bucket-level abort rules (MinIO purges stale uploads globally;
            // Vultr needs a manual sweep), not a failure worth a stack trace.
            _logger.LogWarning(
                "Could not install the abort-incomplete-multipart lifecycle rule on bucket '{BucketName}': " +
                "{Error}. Continuing (expected on MinIO/Vultr).", BucketName, ex.Message);
        }
    }

    //

    public async Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _s3Client.ListBucketsAsync(cancellationToken);
            return response.Buckets != null && response.Buckets.Any(b => b.BucketName == BucketName);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to check if bucket '{BucketName}'.");
        }
    }

    //

    public async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(WriteBytesAsync));

        S3Path.AssertFileName(path);
        path = S3Path.Combine(_rootPath, path);

        var memoryStream = new MemoryStream(bytes);
        try
        {
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = path,
                InputStream = memoryStream,
                ContentType = "application/octet-stream"
            };

            var sw = Stopwatch.StartNew();
            await _s3Client.PutObjectAsync(request, cancellationToken);
            _logger.LogDebug("S3AwsStorage:WriteBytesAsync {elapsed}ms {bytes} bytes {throughput:N0} bytes/sec",
                sw.ElapsedMilliseconds, bytes.Length, BytesPerSecond(bytes.Length, sw));
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to write object '{path}' to bucket '{BucketName}'.");
        }
        finally
        {
            await memoryStream.DisposeAsync();
        }
    }

    //

    public async Task<long> WriteStreamAsync(string path, Stream stream, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(WriteStreamAsync));

        S3Path.AssertFileName(path);

        // The AWS SDK's PutObject needs a known Content-Length. A NON-seekable stream (e.g. an
        // ASP.NET MultipartReaderStream from an incoming request section) has no determinable
        // length, and an InputStream-based PutObject fails with
        // "content would exceed Content-Length". Spill such streams to a temp file and upload from
        // the file (the SDK reads the length from the file via FilePath, exactly like
        // UploadFileAsync). CopyToAsync is chunked, so this stays memory-bounded for large payloads.
        if (!stream.CanSeek)
        {
            var tempFile = Path.Combine(Path.GetTempPath(), "s3-stream-" + Guid.NewGuid().ToString("N"));
            try
            {
                long written;
                await using (var fileStream = new FileStream(
                                 tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await stream.CopyToAsync(fileStream, cancellationToken);
                    written = fileStream.Length;
                }

                // UploadFileAsync applies _rootPath to the (raw) destination key itself, so pass `path` un-combined.
                await UploadFileAsync(tempFile, path, cancellationToken);
                return written;
            }
            finally
            {
                try { File.Delete(tempFile); }
                catch (Exception ex) { _logger.LogDebug(ex, "Failed to delete temp file '{tempFile}'", tempFile); }
            }
        }

        // Seekable stream: upload via TransferUtility so a stream over the 5 GB single-PUT limit goes
        // multipart instead of failing. For a seekable stream the SDK reads it in part-sized chunks
        // rather than buffering the whole object wholesale (only the unseekable/unknown-length case
        // above gets fully buffered), so this stays memory-bounded. Below MinSizeBeforePartUpload
        // (16 MB) it falls back to a single PUT. AutoResetStreamPosition (default true, set
        // explicitly) rewinds to 0 so the whole object is written regardless of the caller's stream
        // position; AutoCloseStream=false keeps the stream owned by the caller.
        path = S3Path.Combine(_rootPath, path);
        var bytesToWrite = stream.Length;

        try
        {
            using var transferUtility = CreateTransferUtility();

            var request = new TransferUtilityUploadRequest
            {
                InputStream = stream,
                BucketName = BucketName,
                Key = path,
                PartSize = UploadPartSizeBytes,
                ContentType = "application/octet-stream",
                AutoCloseStream = false,        // the caller owns the stream
                AutoResetStreamPosition = true, // write the whole object regardless of position
            };

            var sw = Stopwatch.StartNew();
            await transferUtility.UploadAsync(request, cancellationToken);
            _logger.LogDebug("S3AwsStorage:WriteStreamAsync {elapsed}ms {bytes} bytes {throughput:N0} bytes/sec",
                sw.ElapsedMilliseconds, bytesToWrite, BytesPerSecond(bytesToWrite, sw));

            return bytesToWrite;
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to write stream to object '{path}' in bucket '{BucketName}'.");
        }
    }

    //

    public async Task<bool> FileExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(path);
        path = S3Path.Combine(_rootPath, path);

        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = BucketName,
                Key = path
            };

            await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to check if object '{path}' exists in bucket '{BucketName}'.");
        }
    }

    //

    public Task<byte[]> ReadBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        return ReadBytesAsync(path, 0, long.MaxValue, cancellationToken);
    }

    //

    public async Task<byte[]> ReadBytesAsync(string path, long offset, long length, CancellationToken cancellationToken = default)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset cannot be negative");
        }

        if (length < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Length must be greater than 0");
        }

        if (length == long.MaxValue)
        {
            length -= offset;
        }

        // _logger.LogDebug("Requesting bytes from S3: Path={Path}, Offset={Offset}, Length={Length}", path, offset, length);

        S3Path.AssertFileName(path);
        path = S3Path.Combine(_rootPath, path);

        var memoryStream = new MemoryStream();
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = BucketName,
                Key = path,
                ByteRange = new ByteRange(offset, offset + length - 1)
            };

            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            await response.ResponseStream.CopyToAsync(memoryStream, cancellationToken);

            // _logger.LogDebug("Got bytes from S3: Path={Path}, Length={Size}", path, memoryStream.Length);

            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to read object '{path}' from bucket '{BucketName}' with offset {offset} and length {length}.");
        }
        finally
        {
            await memoryStream.DisposeAsync();
        }
    }

    //

    public async Task DeleteFileAsync(string path, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(path);
        path = S3Path.Combine(_rootPath, path);

        try
        {
            var request = new DeleteObjectRequest
            {
                BucketName = BucketName,
                Key = path
            };

            await _s3Client.DeleteObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Ignore if file doesn't exist
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to delete object '{path}' from bucket '{BucketName}'.");
        }
    }

    //

    // SEB:NOTE this will not delete versioned objects (if versioning is enabled on the bucket).
    public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFolderName(path);
        path = S3Path.Combine(_rootPath, path);
        await DeletePrefixInternalAsync(path, cancellationToken);
    }

    // Delete every object whose key starts with the given (non-folder) prefix.
    // Used by inbox cleanup to remove all "{fileId:N}." staging objects for one fileId.
    public async Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        prefix = S3Path.Combine(_rootPath, prefix);
        await DeletePrefixInternalAsync(prefix, cancellationToken);
    }

    private async Task DeletePrefixInternalAsync(string prefix, CancellationToken cancellationToken)
    {
        // S3 doesn't have directories, so we list and delete all objects with the given prefix.
        try
        {
            bool isTruncated;
            string? continuationToken = null;
            do
            {
                var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = BucketName,
                    ContinuationToken = continuationToken,
                    MaxKeys = 1000,
                    Prefix = prefix,
                }, cancellationToken);

                if (listResponse.S3Objects?.Count > 0)
                {
                    var deleteRequest = new DeleteObjectsRequest
                    {
                        BucketName = BucketName,
                        Objects = listResponse.S3Objects
                            .Select(o => new KeyVersion { Key = o.Key })
                            .ToList()
                    };
                    await _s3Client.DeleteObjectsAsync(deleteRequest, cancellationToken);
                }

                continuationToken = listResponse.NextContinuationToken;
                isTruncated = listResponse.IsTruncated ?? false;
            }
            while (isTruncated);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed delete all objects from '{prefix}' in bucket '{BucketName}'.");
        }
    }


    //

    public string GetFullKey(string path) => S3Path.Combine(_rootPath, path);

    //

    public async Task CopyFromBucketAsync(string sourceBucket, string sourceKey, string destKey, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(destKey);
        destKey = S3Path.Combine(_rootPath, destKey);

        var request = new CopyObjectRequest
        {
            SourceBucket = sourceBucket,
            SourceKey = sourceKey,   // RAW: caller already built the full source key
            DestinationBucket = BucketName,
            DestinationKey = destKey
        };

        try
        {
            var sw = Stopwatch.StartNew();
            await _s3Client.CopyObjectAsync(request, cancellationToken);
            _logger.LogDebug("S3AwsStorage:CopyFromBucketAsync {elapsed}ms src={sourceBucket}/{sourceKey} dst={BucketName}/{destKey}",
                sw.ElapsedMilliseconds, sourceBucket, sourceKey, BucketName, destKey);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to copy object from '{sourceBucket}/{sourceKey}' to '{BucketName}/{destKey}'.");
        }
    }

    //

    public async Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        if (string.Equals(srcPath, dstPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Source and destination paths cannot be the same", nameof(dstPath));
        }

        S3Path.AssertFileName(srcPath);
        S3Path.AssertFileName(dstPath);
        srcPath = S3Path.Combine(_rootPath, srcPath);
        dstPath = S3Path.Combine(_rootPath, dstPath);

        var request = new CopyObjectRequest
        {
            SourceBucket = BucketName,
            SourceKey = srcPath,
            DestinationBucket = BucketName,
            DestinationKey = dstPath
        };

        try
        {
            var sw = Stopwatch.StartNew();
            await _s3Client.CopyObjectAsync(request, cancellationToken);
            _logger.LogDebug("S3AwsStorage:CopyFileAsync {elapsed}ms", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to copy object from '{srcPath}' to '{dstPath}' in bucket '{BucketName}'.");
        }

    }

    //

    public async Task MoveFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        await CopyFileAsync(srcPath, dstPath, cancellationToken);
        await DeleteFileAsync(srcPath, cancellationToken);
    }

    //

    // Builds a TransferUtility wired with the flagged upload defaults (concurrency lives here; the
    // per-request PartSize is set at each call site). TransferUtility does multipart automatically
    // once the payload exceeds MinSizeBeforePartUpload (SDK default 16 MB), so callers are NOT bound
    // by the 5 GB single-PUT limit; smaller payloads fall back to a single PUT.
    // NOTE: a TransferUtility built from an injected IAmazonS3 does NOT dispose that client on its
    // own Dispose, so wrapping the returned instance in `using` is safe for the shared singleton.
    private TransferUtility CreateTransferUtility()
    {
        return new TransferUtility(_s3Client, new TransferUtilityConfig
        {
            ConcurrentServiceRequests = UploadConcurrencyLimit,
            // MinSizeBeforePartUpload left at the SDK default (16 MB).
        });
    }

    //

    public async Task UploadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(dstPath);
        dstPath = S3Path.Combine(_rootPath, dstPath);

        var bytesToWrite = new FileInfo(srcPath).Length;

        try
        {
            // Multipart-on-demand handles the 5GB+ video case; the 5 GB single-PUT limit
            // does not apply here. On a thrown exception the SDK aborts the multipart upload it
            // started (the crash-not-exception orphaned-parts case is still open).
            using var transferUtility = CreateTransferUtility();

            var request = new TransferUtilityUploadRequest
            {
                FilePath = srcPath,
                BucketName = BucketName,
                Key = dstPath,
                PartSize = UploadPartSizeBytes,
                ContentType = "application/octet-stream" // correct: opaque/encrypted bytes; real type served from PayloadDescriptor, not S3
            };

            var sw = Stopwatch.StartNew();
            await transferUtility.UploadAsync(request, cancellationToken);
            _logger.LogDebug("S3AwsStorage:UploadFileAsync {elapsed}ms {bytes} bytes {throughput:N0} bytes/sec",
                sw.ElapsedMilliseconds, bytesToWrite, BytesPerSecond(bytesToWrite, sw));
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to upload file '{srcPath}' to '{dstPath}' in bucket '{BucketName}.");
        }
    }

    //

    public async Task DownloadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(srcPath);
        srcPath = S3Path.Combine(_rootPath, srcPath);

        Directory.CreateDirectory(Path.GetDirectoryName(dstPath) ?? throw new InvalidOperationException());

        try
        {
            var request = new GetObjectRequest
            {
                BucketName = BucketName,
                Key = srcPath
            };

            using var response = await _s3Client.GetObjectAsync(request, cancellationToken);
            await response.WriteResponseStreamToFileAsync(dstPath, false, cancellationToken);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to download object '{srcPath}' to '{dstPath}'.");
        }
    }

    //

    public async Task<long> FileLengthAsync(string path, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(path);
        path = S3Path.Combine(_rootPath, path);

        try
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = BucketName,
                Key = path
            };

            var metadata = await _s3Client.GetObjectMetadataAsync(request, cancellationToken);
            return metadata.ContentLength;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw CreateS3StorageException(ex, $"File '{path}' does not exist in bucket '{BucketName}'.");
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to get file size of '{path}' in bucket '{BucketName}'.");
        }
    }

    //

    private static double BytesPerSecond(long bytes, Stopwatch sw)
    {
        var seconds = sw.Elapsed.TotalSeconds;
        return seconds > 0 ? bytes / seconds : 0;
    }

    //

    private const string AbortIncompleteMultipartRuleId = "odin-abort-incomplete-multipart";

    // Days after which S3 auto-aborts an incomplete multipart upload: a
    // crash-orphaned set of parts that bills as storage but is invisible as an object. Must exceed
    // the longest legitimate upload; a single upload (even 5GB+) finishes in minutes, so 7 days is
    // comfortably safe and the conventional default (min is 1; S3 sweeps ~once/day). Installed on
    // every bucket by CreateBucketAsync (best-effort). Provider support varies: AWS S3 / Hetzner /
    // Linode honor it; Vultr has no lifecycle (needs a manual sweep); MinIO rejects bucket-level
    // abort rules by design and purges stale uploads globally (api stale_uploads_expiry) instead --
    // so the install is allowed to fail without breaking startup.
    private const int AbortIncompleteMultipartUploadDays = 7;

    // Always-on backstop installed on every bucket by CreateBucketAsync: S3 auto-aborts incomplete
    // multipart uploads older than AbortIncompleteMultipartUploadDays, reaping parts left behind when
    // a process dies mid-upload (no in-process abort ran). Scoped to this storage's root prefix.
    private Task EnsureAbortIncompleteMultipartLifecycleAsync(CancellationToken cancellationToken)
    {
        var prefix = string.IsNullOrEmpty(_rootPath) ? "" : _rootPath + "/";
        var rule = new LifecycleRule
        {
            Id = AbortIncompleteMultipartRuleId,
            Status = LifecycleRuleStatus.Enabled,
            Filter = new LifecycleFilter
            {
                LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = prefix }
            },
            AbortIncompleteMultipartUpload = new LifecycleRuleAbortIncompleteMultipartUpload
            {
                DaysAfterInitiation = AbortIncompleteMultipartUploadDays
            }
        };
        return ReconcileLifecycleRuleAsync(AbortIncompleteMultipartRuleId, rule, cancellationToken);
    }

    // Idempotently upserts a single lifecycle rule by id, leaving every foreign rule in place.
    private async Task ReconcileLifecycleRuleAsync(string ruleId, LifecycleRule rule, CancellationToken cancellationToken)
    {
        List<LifecycleRule> rules;
        try
        {
            var existing = await _s3Client.GetLifecycleConfigurationAsync(
                new GetLifecycleConfigurationRequest { BucketName = BucketName }, cancellationToken);
            rules = existing.Configuration?.Rules ?? new List<LifecycleRule>();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            rules = new List<LifecycleRule>();
        }

        // Replace any previous copy of our rule; foreign rules are left untouched.
        rules = rules.Where(r => r.Id != ruleId).ToList();
        rules.Add(rule);

        await _s3Client.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
        {
            BucketName = BucketName,
            Configuration = new LifecycleConfiguration { Rules = rules }
        }, cancellationToken);
    }

    //

    private S3StorageException CreateS3StorageException(Exception exception, string message)
    {
        var error = exception.Message;

        if (string.IsNullOrEmpty(error))
        {
            // This is a freaking weird, Amazon. Wth...
            if (exception.InnerException is Amazon.Runtime.Internal.HttpErrorResponseException httpException)
            {
                error = $": S3 HTTP status={httpException.Response.StatusCode}";
            }
            else
            {
                error = exception.InnerException?.Message ?? ": Unknown error";
            }
        }

        return new S3StorageException($"{message} {error}", exception);
    }

    //

}

//

public static class S3AwsStorageExtensions
{
    public static IServiceCollection AddAmazonS3Client(
        this IServiceCollection services,
        string accessKey,
        string secretAccessKey,
        string serviceUrl,
        string region,
        bool forcePathStyle)
    {
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            accessKey,
            secretAccessKey,
            new AmazonS3Config
            {
                ServiceURL = serviceUrl,
                AuthenticationRegion = region,
                ForcePathStyle = forcePathStyle,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                MaxErrorRetry = 2,
                Timeout = TimeSpan.FromMinutes(5),
            }));
        return services;
    }
}

