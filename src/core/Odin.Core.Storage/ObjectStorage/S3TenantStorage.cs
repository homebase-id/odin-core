using System;
using Autofac;
using Microsoft.Extensions.Logging;
using Minio;
using Odin.Core.Identity;

namespace Odin.Core.Storage.ObjectStorage;

#nullable enable

public interface IS3TenantStorage : IS3Storage
{
}

public class S3TenantStorage(
    ILogger<S3TenantStorage> logger,
    IMinioClient minioClient,
    string bucketName,
    OdinIdentity odinIdentity)
    : S3Storage(logger, minioClient, bucketName, "tenants/" + odinIdentity.Id + "/"), IS3TenantStorage
{
}

public static class S3TenantStorageExtensions
{
    public static ContainerBuilder AddS3TenantStorage(this ContainerBuilder cb, string bucketName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bucketName, nameof(bucketName));

        cb.Register(c =>
            new S3TenantStorage(
                c.Resolve<ILogger<S3TenantStorage>>(),
                c.Resolve<IMinioClient>(),
                bucketName,
                c.Resolve<OdinIdentity>()))
            .As<IS3TenantStorage>()
            .AsSelf();

        return cb;
    }
}
