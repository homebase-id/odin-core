using System;
using System.Threading.Tasks;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Minio;
using Minio.DataModel.Args;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Core.Storage.ObjectStorage;
using Odin.Test.Helpers.Secrets;

namespace Odin.Core.Storage.Tests.ObjectStorage;

#nullable enable

public class S3SystemStorageTests
{
    private readonly string _bucketName = $"test-{Guid.NewGuid():N}";
    private IServiceProvider _services = null!;
    private ILifetimeScope _container = null!;

    [SetUp]
    public async Task SetUp()
    {
        AddServices();
        var minioClient = _services.GetRequiredService<IMinioClient>();
        await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));
    }

    [TearDown]
    public async Task TearDown()
    {
        var minioClient = _services.GetRequiredService<IMinioClient>();
        await minioClient.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(_bucketName));
    }

    private void AddServices()
    {
        TestSecrets.Load();

        var accessKey = Environment.GetEnvironmentVariable("ODIN_S3_ACCESS_KEY") ?? throw new Exception("missing ODIN_S3_ACCESS_KEY");
        var secretAccessKey = Environment.GetEnvironmentVariable("ODIN_S3_SECRET_ACCESS_KEY") ?? throw new Exception("missing ODIN_S3_SECRET_ACCESS_KEY");

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMinioClient(
            "hel1.your-objectstorage.com",
            accessKey,
            secretAccessKey,
            "hel1");

        services.AddS3SystemStorage(_bucketName);

        var builder = new ContainerBuilder();
        builder.RegisterInstance(new OdinIdentity(Guid.NewGuid(), "foo.bar"));
        builder.AddS3TenantStorage(_bucketName);
        builder.Populate(services);

        _container = builder.Build();
        _services = services.BuildServiceProvider();
    }

    //

    [Test, Explicit]
    public async Task IsShouldCreateCorrectSystemRootPath()
    {
        var bucket = _services.GetRequiredService<IS3SystemStorage>();
        var bucketExists = await bucket.BucketExistsAsync();
        Assert.That(bucketExists, Is.True);
        Assert.That(bucket.RootPath, Is.EqualTo("system/"));
    }

    //

    [Test, Explicit]
    public async Task IsShouldCreateCorrectTenantRootPath()
    {
        var bucket = _container.Resolve<IS3TenantStorage>();
        var bucketExists = await bucket.BucketExistsAsync();
        Assert.That(bucketExists, Is.True);

        var odinIdentity = _container.Resolve<OdinIdentity>();
        Assert.That(bucket.RootPath, Is.EqualTo($"tenants/{odinIdentity.Id}/"));
    }

    //


}