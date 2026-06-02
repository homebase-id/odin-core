using System;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3PayloadStorage : IS3Storage
{
}

public sealed class S3AwsPayloadStorage(
    ILogger<S3AwsPayloadStorage> logger, IAmazonS3 awsClient, string bucketName, string rootPath = "")
    : S3AwsStorage(logger, awsClient, bucketName, rootPath), IS3PayloadStorage
{
}

//

public static class S3AwsPayloadStorageExtensions
{
    public static IServiceCollection AddS3AwsPayloadStorage(
        this IServiceCollection services,
        string bucketName,
        string rootPath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));
        ArgumentNullException.ThrowIfNull(rootPath, nameof(rootPath));

        services.AddSingleton<IS3PayloadStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<S3AwsPayloadStorage>>();
            var awsClient = sp.GetRequiredService<IAmazonS3>();
            return new S3AwsPayloadStorage(logger, awsClient, bucketName, rootPath);
        });

        return services;
    }
}
