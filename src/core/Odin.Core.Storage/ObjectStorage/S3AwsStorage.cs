using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.ObjectStorage;

public class S3AwsStorage
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

}

//

public static class S3AwsStorageExtensions
{
    public static IServiceCollection AddAwsS3Client(
        this IServiceCollection services,
        string endpoint,
        string accessKey,
        string secretAccessKey,
        string region)
    {
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client(
            accessKey,
            secretAccessKey,
            new AmazonS3Config
            {
                ServiceURL = endpoint,
                ForcePathStyle = true, // Required for S3-compatible services
                UseHttp = false,       // Always use HTTPS
                RegionEndpoint = !string.IsNullOrEmpty(region) ?
                    Amazon.RegionEndpoint.GetBySystemName(region) : null
            }));

        return services;
    }
}


