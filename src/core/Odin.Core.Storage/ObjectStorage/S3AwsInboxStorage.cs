using System;
using Amazon.S3;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3InboxStorage : IS3Storage
{
}

public sealed class S3AwsInboxStorage(
    ILogger<S3AwsInboxStorage> logger, IAmazonS3 awsClient, string bucketName, string rootPath = "")
    : S3AwsStorage(logger, awsClient, bucketName, rootPath), IS3InboxStorage
{
}

//

public static class S3AwsInboxStorageExtensions
{
    // Registers IS3InboxStorage over the shared IAmazonS3 singleton.
    // The caller must have already registered IAmazonS3 via AddAmazonS3Client.
    public static IServiceCollection AddS3AwsInboxStorage(
        this IServiceCollection services,
        string bucketName,
        string rootPath = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));

        services.AddSingleton<IS3InboxStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<S3AwsInboxStorage>>();
            var awsClient = sp.GetRequiredService<IAmazonS3>();
            return new S3AwsInboxStorage(logger, awsClient, bucketName, rootPath);
        });

        return services;
    }
}
