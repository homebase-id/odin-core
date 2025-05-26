using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3PayloadStorage : IS3Storage
{
}

public sealed class S3MinioPayloadStorage(
    ILogger<S3MinioPayloadStorage> logger,
    IMinioClient minioClient,
    string bucketName)
    : S3MinioStorage(logger, minioClient, bucketName), IS3PayloadStorage
{
}

public static class S3MinioPayloadStorageExtensions
{
    public static IServiceCollection AddS3MinioPayloadStorage(
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

        services.AddMinioClient(
            endpoint,
            accessKey,
            secretAccessKey,
            region);

        services.AddSingleton<IS3PayloadStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<S3MinioPayloadStorage>>();
            var minioClient = sp.GetRequiredService<IMinioClient>();
            return new S3MinioPayloadStorage(logger, minioClient, bucketName);
        });

        return services;
    }
}
