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

public class S3AwsStorage : IS3Storage
{
    private readonly ILogger<S3AwsStorage> _logger;
    private readonly IAmazonS3 _s3Client;

    public string BucketName { get; }

    public S3AwsStorage(ILogger<S3AwsStorage> logger, IAmazonS3 s3Client, string bucketName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));
        _logger = logger;
        _s3Client = s3Client;
        BucketName = bucketName;
    }

    //

    public async Task<bool> BucketExistsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(BucketExistsAsync));
        try
        {
            var response = await _s3Client.ListBucketsAsync(cancellationToken);
            return response.Buckets.Any(b => b.BucketName == BucketName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if bucket '{Bucket}' exists: {Message}", BucketName, ex.Message);
            return false;
        }
    }

    //

    public async Task WriteBytesAsync(string path, byte[] bytes, CancellationToken cancellationToken = default)
    {
        _logger.LogTrace(nameof(WriteBytesAsync));

        S3Path.AssertFileName(path);
        path = S3Path.Combine(path);

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
            _logger.LogError(ex, "Failed to write object '{Path}' to bucket '{Bucket}': {Message}",
                path, BucketName, ex.Message);
            throw;
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
        path = S3Path.Combine(path);

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

        S3Path.AssertFileName(path);
        path = S3Path.Combine(path);

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
            return memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read object '{Path}' from bucket '{Bucket}' with offset {Offset} and length {Length}: {Message}",
                path, BucketName, offset, length, ex.Message);
            throw;
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
        path = S3Path.Combine(path);

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
    }

    //

    public async Task CopyFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(srcPath);
        S3Path.AssertFileName(dstPath);
        srcPath = S3Path.Combine(srcPath);
        dstPath = S3Path.Combine(dstPath);

        var request = new CopyObjectRequest
        {
            SourceBucket = BucketName,
            SourceKey = srcPath,
            DestinationBucket = BucketName,
            DestinationKey = dstPath
        };

        await _s3Client.CopyObjectAsync(request, cancellationToken);
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
        dstPath = S3Path.Combine(dstPath);

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
            _logger.LogError(ex, "Failed to upload file '{SrcPath}' to '{DstPath}' in bucket '{Bucket}': {Message}",
                srcPath, dstPath, BucketName, ex.Message);
            throw;
        }
    }

    //

    public async Task DownloadFileAsync(string srcPath, string dstPath, CancellationToken cancellationToken = default)
    {
        S3Path.AssertFileName(srcPath);
        srcPath = S3Path.Combine(srcPath);

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
            _logger.LogError(ex, "Failed to download object '{SrcPath}' to '{DstPath}': {Message}",
                srcPath, dstPath, ex.Message);
            throw;
        }
    }

    //

}

//

public static class S3AwsStorageExtensions
{
    // SEB:NOTE S3 settings seem to be different for different providers. So for now we hardcode the Hetzner settings.
    public static AmazonS3Config GetHetznerConfig(string endpoint, string region)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint, nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(region, nameof(region));

        if (!endpoint.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = "https://" + endpoint;
        }

        return new AmazonS3Config
        {
            ServiceURL = endpoint,
            AuthenticationRegion = region,
            ForcePathStyle = false,
            UseHttp = false,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
        };
    }

    //

    public static IServiceCollection AddAmazonS3Client(
        this IServiceCollection services,
        string endpoint,
        string accessKey,
        string secretAccessKey,
        string region)
    {
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            accessKey,
            secretAccessKey,
            GetHetznerConfig(endpoint, region)));

        return services;
    }

    //


}
