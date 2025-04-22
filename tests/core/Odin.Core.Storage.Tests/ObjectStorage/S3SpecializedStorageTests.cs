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
    private string _bucketName = "";
    private IServiceProvider _services = null!;
    private ILifetimeScope _container = null!;

    [SetUp]
    public async Task SetUp()
    {
        TestSecrets.Load();

        var accessKey = Environment.GetEnvironmentVariable("ODIN_S3_ACCESS_KEY");
        var secretAccessKey = Environment.GetEnvironmentVariable("ODIN_S3_SECRET_ACCESS_KEY");

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            Assert.Ignore("Environment variable ODIN_S3_ACCESS_KEY or ODIN_S3_SECRET_ACCESS_KEY is not set");
        }

        _bucketName = $"zz-ci-test-{Guid.NewGuid():N}";
        AddServices(accessKey, secretAccessKey);

        var minioClient = _services.GetRequiredService<IMinioClient>();
        await minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));
    }

    [TearDown]
    public async Task TearDown()
    {
        if (_services != null!)
        {
            var minioClient = _services.GetRequiredService<IMinioClient>();
            await minioClient.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(_bucketName));
        }
    }

    private void AddServices(string accessKey, string secretAccessKey)
    {
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

    [Test]
    public async Task IsShouldCreateCorrectSystemRootPath()
    {
        var bucket = _services.GetRequiredService<IS3SystemStorage>();
        var bucketExists = await bucket.BucketExistsAsync();
        Assert.That(bucketExists, Is.True);
        Assert.That(bucket.RootPath, Is.EqualTo("system/"));
    }

    //

    [Test]
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