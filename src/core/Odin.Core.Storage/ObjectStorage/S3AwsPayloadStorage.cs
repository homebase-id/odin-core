using System;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public sealed class S3AwsPayloadStorage(ILogger<S3AwsPayloadStorage> logger, IAmazonS3 awsClient, string bucketName)
    : S3AwsStorage(logger, awsClient, bucketName), IS3PayloadStorage
{
}

//

public static class S3AwsPayloadStorageExtensions
{
    public static IServiceCollection AddS3AwsPayloadStorage(
        this IServiceCollection services,
        string endpoint,
        string accessKey,
        string secretAccessKey,
        string region,
        string bucketName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint, nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(accessKey, nameof(accessKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(secretAccessKey, nameof(secretAccessKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(region, nameof(region));
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));

        services.AddAwsS3Client(
            endpoint,
            accessKey,
            secretAccessKey,
            region);

        services.AddSingleton<IS3PayloadStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<S3AwsPayloadStorage>>();
            var awsClient = sp.GetRequiredService<IAmazonS3>();
            return new S3AwsPayloadStorage(logger, awsClient, bucketName);
        });

        return services;
    }
}
