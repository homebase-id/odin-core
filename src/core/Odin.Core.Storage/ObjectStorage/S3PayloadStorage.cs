using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3PayloadStorage : IS3Storage
{
}

public sealed class S3PayloadStorage(
    ILogger<S3PayloadStorage> logger,
    IMinioClient minioClient,
    string bucketName)
    : S3Storage(logger, minioClient, bucketName), IS3PayloadStorage
{
}

public static class S3PayloadStorageExtensions
{
    public static IServiceCollection AddS3PayloadStorage(
        this IServiceCollection services,
        string bucketName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));

        services.AddSingleton<S3PayloadStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<S3PayloadStorage>>();
            var minioClient = sp.GetRequiredService<IMinioClient>();
            return new S3PayloadStorage(logger, minioClient, bucketName);
        });

        services.AddSingleton<IS3PayloadStorage>(sp => sp.GetRequiredService<S3PayloadStorage>());

        return services;
    }
}
