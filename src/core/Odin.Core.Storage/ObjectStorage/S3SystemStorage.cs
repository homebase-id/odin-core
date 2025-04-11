using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Minio;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3SystemStorage : IS3Storage
{
}

public sealed class S3SystemStorage(
    ILogger<S3SystemStorage> logger, IMinioClient minioClient, string bucketName)
    : S3Storage(logger, minioClient, bucketName, "system/"), IS3SystemStorage
{
}

public static class S3SystemStorageExtensions
{
    public static IServiceCollection AddS3SystemStorage(this IServiceCollection services, string bucketName)
    {
        services.AddSingleton<S3SystemStorage>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<S3SystemStorage>>();
            var minioClient = sp.GetRequiredService<IMinioClient>();
            return new S3SystemStorage(logger, minioClient, bucketName);
        });

        services.AddSingleton<IS3SystemStorage>(sp => sp.GetRequiredService<S3SystemStorage>());

        return services;
    }
}
