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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

//
// SEB:NOTE
// There is not TryRetry here because S3 SDK already has retry logic built-in.
// It defaults to 3 retries with exponential backoff.
// Can be overridden by configuring the AmazonS3Config when creating the client.
//

public class S3AwsStorage : IS3Storage
{
    private readonly ILogger<S3AwsStorage> _logger;
    private readonly IAmazonS3 _s3Client;
    private readonly string _rootPath;

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
        if (await BucketExistsAsync(cancellationToken))
        {
            return;
        }

        try
        {
            await _s3Client.PutBucketAsync(BucketName, cancellationToken);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Create bucket '{BucketName} failed'.");
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
        path = S3Path.Combine(_rootPath, path);

        // The S3 SDK uploads the entire seekable stream (it rewinds to the start), so the
        // bytes written equal the full stream length, regardless of the current position.
        var bytesToWrite = stream.CanSeek ? stream.Length : 0;

        try
        {
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = path,
                InputStream = stream,
                ContentType = "application/octet-stream",
                AutoCloseStream = false, // the caller owns the stream
            };

            var sw = Stopwatch.StartNew();
            await _s3Client.PutObjectAsync(request, cancellationToken);
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

    public async Task UploadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(dstPath);
        dstPath = S3Path.Combine(_rootPath, dstPath);

        var bytesToWrite = new FileInfo(srcPath).Length;

        try
        {
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = dstPath,
                FilePath = srcPath,
                ContentType = "application/octet-stream"
            };

            var sw = Stopwatch.StartNew();
            await _s3Client.PutObjectAsync(request, cancellationToken);
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

    private const string ExpirationLifecycleRuleId = "odin-inbox-expiration";

    // Reconciles the S3 lifecycle rule that auto-expires objects under this storage's root prefix.
    // S3 itself (not the app) deletes objects older than expirationDays, acting as a backstop so
    // orphaned inbox items get cleaned up even if the normal delete path never runs.
    //
    //   expirationDays > 0  -> install/update an Enabled rule (id "odin-inbox-expiration") that
    //                          expires objects under _rootPath after that many days.
    //   expirationDays <= 0 -> remove our rule (turns expiration off).
    //
    // Idempotent: reads the current config, strips any previous copy of our rule, then re-adds the
    // current one (if any). Rules owned by anyone else on the bucket are left in place. The whole
    // lifecycle config is DELETEd only when our rule was the sole entry, and only if it actually
    // existed, so a bucket that never had config is left untouched.
    public async Task EnsureExpirationLifecycleAsync(int expirationDays, CancellationToken cancellationToken = default)
    {
        try
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

            var hadOurRule = rules.Any(r => r.Id == ExpirationLifecycleRuleId);
            rules = rules.Where(r => r.Id != ExpirationLifecycleRuleId).ToList();

            if (expirationDays > 0)
            {
                var prefix = string.IsNullOrEmpty(_rootPath) ? "" : _rootPath + "/";
                rules.Add(new LifecycleRule
                {
                    Id = ExpirationLifecycleRuleId,
                    Status = LifecycleRuleStatus.Enabled,
                    Filter = new LifecycleFilter
                    {
                        LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = prefix }
                    },
                    Expiration = new LifecycleRuleExpiration { Days = expirationDays }
                });
            }

            if (rules.Count == 0)
            {
                // Only delete the whole config if there was actually something we removed.
                if (hadOurRule)
                {
                    await _s3Client.DeleteLifecycleConfigurationAsync(
                        new DeleteLifecycleConfigurationRequest { BucketName = BucketName }, cancellationToken);
                }
                // else: bucket already had no relevant config — nothing to do.
            }
            else
            {
                await _s3Client.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
                {
                    BucketName = BucketName,
                    Configuration = new LifecycleConfiguration { Rules = rules }
                }, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to reconcile expiration lifecycle on bucket '{BucketName}'.");
        }
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

