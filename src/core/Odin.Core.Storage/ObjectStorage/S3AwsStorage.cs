using System;
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
            throw CreateS3StorageException(ex, $"Create bucket '{BucketName} failed'");
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
            throw CreateS3StorageException(ex, $"Failed to check if bucket '{BucketName}'");
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

            await _s3Client.PutObjectAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to write object '{path}' to bucket '{BucketName}'");
        }
        finally
        {
            await memoryStream.DisposeAsync();
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
                $"Failed to check if object '{path}' exists in bucket '{BucketName}'");
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
                $"Failed to read object '{path}' from bucket '{BucketName}' with offset {offset} and length {length}");
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
                $"Failed to delete object '{path}' from bucket '{BucketName}'");
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
            await _s3Client.CopyObjectAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to copy object from '{srcPath}' to '{dstPath}' in bucket '{BucketName}'");
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

        try
        {
            var request = new PutObjectRequest
            {
                BucketName = BucketName,
                Key = dstPath,
                FilePath = srcPath,
                ContentType = "application/octet-stream"
            };

            await _s3Client.PutObjectAsync(request, cancellationToken);
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex,
                $"Failed to upload file '{srcPath}' to '{dstPath}' in bucket '{BucketName}");
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
            throw CreateS3StorageException(ex, $"Failed to download object '{srcPath}' to '{dstPath}'");
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
            throw CreateS3StorageException(ex, $"File '{path}' does not exist in bucket '{BucketName}'");
        }
        catch (Exception ex)
        {
            throw CreateS3StorageException(ex, $"Failed to get file size of '{path}' in bucket '{BucketName}'");
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

        return new S3StorageException($"{message}{error}", exception);
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
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
            }));
        return services;
    }
}

