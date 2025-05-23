using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Moq;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Test.Helpers.Secrets;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

public class PayloadS3ReaderWriterTests : PayloadReaderWriterBaseTestFixture
{
    private OdinConfiguration _config = null!;
    private TenantContext _tenantContext = null!;
    private TenantPathManager _tenantPathManager = null!;
    private IMinioClient _minioClient = null!;
    private S3PayloadStorage _s3PayloadStorage = null!;

    [SetUp]
    public async Task Setup()
    {
        BaseSetup();

        TestSecrets.Load();

        var accessKey = Environment.GetEnvironmentVariable("ODIN_S3_ACCESS_KEY");
        var secretAccessKey = Environment.GetEnvironmentVariable("ODIN_S3_SECRET_ACCESS_KEY");

        if (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretAccessKey))
        {
            Assert.Ignore("Environment variable ODIN_S3_ACCESS_KEY or ODIN_S3_SECRET_ACCESS_KEY is not set");
        }

        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                TenantDataRootPath = Path.Combine(TestRootPath, "tenants"),
            },
            S3PayloadStorage = new OdinConfiguration.S3PayloadStorageSection
            {
                Enabled = true,
                BucketName = $"zz-ci-test-{Guid.NewGuid():N}",
                Region = "hel1",
                Endpoint = "hel1.your-objectstorage.com",
                AccessKey = accessKey,
                SecretAccessKey = secretAccessKey,
            }
        };

        var tenantId = Guid.NewGuid();
        var domain = "frodo.me";
        _tenantContext = new TenantContext(
            tenantId,
            new OdinId(domain),
            new TenantPathManager(_config, tenantId),
            firstRunToken: null,
            isPreconfigured: true,
            markedForDeletionDate: null
        );
        _tenantPathManager = _tenantContext.TenantPathManager;

        _minioClient = new MinioClient()
            .WithEndpoint(_config.S3PayloadStorage.Endpoint)
            .WithCredentials(_config.S3PayloadStorage.AccessKey, _config.S3PayloadStorage.SecretAccessKey)
            .WithRegion(_config.S3PayloadStorage.Region)
            .WithSSL()
            .Build();

        var bucketName = _config.S3PayloadStorage.BucketName;
        await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));

        _s3PayloadStorage = new S3PayloadStorage(
            new Mock<ILogger<S3PayloadStorage>>().Object,
            _minioClient,
            bucketName);
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            if (_minioClient != null!)
            {
                // Remove all objects
                var listArgs = new ListObjectsArgs().WithBucket(_config.S3PayloadStorage.BucketName).WithRecursive(true);
                await foreach (var item in _minioClient.ListObjectsEnumAsync(listArgs))
                {
                    await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                        .WithBucket(_config.S3PayloadStorage.BucketName)
                        .WithObject(item.Key));
                }

                // Remove bucket
                await _minioClient.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(_config.S3PayloadStorage.BucketName));
            }
        }
        finally
        {
            BaseTearDown();
        }
    }

    //

    private async Task CreateFileAsync(string filePath, string content = "hello")
    {
        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);
        var someBytes = content.ToUtf8ByteArray();
        await rw.WriteFileAsync(filePath, someBytes);
    }

    //

    [Test]
    public async Task WriteFileAsync_ShouldWriteFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var path = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        Assert.That(path, Does.StartWith(_tenantContext.DotYouRegistryId.ToString()));

        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(path, someBytes);

        await Task.Delay(100);

        var exists = await _s3PayloadStorage.FileExistsAsync(path);
        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public async Task DeleteFileAsync_ShouldDeleteFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var path = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        Assert.That(path, Does.StartWith(_tenantContext.DotYouRegistryId.ToString()));

        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(path, someBytes);

        await Task.Delay(100);

        var exists = await _s3PayloadStorage.FileExistsAsync(path);
        Assert.That(exists, Is.True);

        await rw.DeleteFileAsync(path);

        await Task.Delay(100);

        exists = await _s3PayloadStorage.FileExistsAsync(path);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    public async Task FileExistsAsync_ShouldCheckIfFileExists()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var path = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        Assert.That(path, Does.StartWith(_tenantContext.DotYouRegistryId.ToString()));

        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(path, someBytes);

        await Task.Delay(100);

        var exists = await rw.FileExistsAsync(path);
        Assert.That(exists, Is.True);

        await rw.DeleteFileAsync(path);
        await Task.Delay(100);

        exists = await rw.FileExistsAsync(path);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    public async Task MoveFileAsync_ShouldMoveTheFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var srcPath = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        Assert.That(srcPath, Does.StartWith(_tenantContext.DotYouRegistryId.ToString()));

        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(srcPath, someBytes);

        await Task.Delay(100);

        var exists = await rw.FileExistsAsync(srcPath);
        Assert.That(exists, Is.True);

        var otherDriveId = Guid.NewGuid();
        var dstPath = _tenantPathManager.GetPayloadDirectoryAndFileName(otherDriveId, fileId, appKey, timestamp);
        Assert.That(dstPath, Does.StartWith(_tenantContext.DotYouRegistryId.ToString()));

        exists = await rw.FileExistsAsync(dstPath);
        Assert.That(exists, Is.False);

        await rw.MoveFileAsync(srcPath, dstPath);

        await Task.Delay(100);

        exists = await rw.FileExistsAsync(srcPath);
        Assert.That(exists, Is.False);

        exists = await rw.FileExistsAsync(dstPath);
        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public Task MoveFileAsync_ShouldThrowOnMissingSrcFile()
    {
        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var srcFile = Path.Combine(TestRootPath, Guid.NewGuid().ToString());
        var dstFile = Path.Combine(TestRootPath, Guid.NewGuid().ToString());

        Assert.ThrowsAsync<PayloadReaderWriterException>(() => rw.MoveFileAsync(srcFile, dstFile));

        return Task.CompletedTask;
    }

    //

    [Test]
    public async Task GetFilesInDirectoryAsync_ShouldGetFilesInDirectory()
    {
        var root = Path.Combine(_tenantPathManager.RootPayloadsPath, "frodo/");

        await CreateFileAsync(S3Path.Combine(root, "file1.foo"));
        await CreateFileAsync(S3Path.Combine(root, "file2.bar"));
        await CreateFileAsync(S3Path.Combine(root, "subdir", "file3.foo"));

        await Task.Delay(100);

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var files = await rw.GetFilesInDirectoryAsync(root);
        Assert.That(files.Length, Is.EqualTo(2));
        Assert.That(files, Does.Contain(S3Path.Combine(root, "file1.foo")));
        Assert.That(files, Does.Contain(S3Path.Combine(root, "file2.bar")));
    }

    //

    [Test]
    public async Task CreateDirectoryAsync_ShouldCreateDirectory()
    {
        var root = Path.Combine(_tenantPathManager.RootPayloadsPath, "frodo/sam");
        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);
        await rw.CreateDirectoryAsync(root);
        Assert.Pass(); // No-op: S3 does not have directories in the same way as a file system.
    }

    //

    [Test]
    public async Task CopyPayloadFileAsync_ShouldCopyFileToPayloads()
    {
        var srcFile = Path.Combine(TestRootPath, "file1.foo");
        CreateFile(srcFile);

        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var dstFile = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        await rw.CopyPayloadFileAsync(srcFile, dstFile);
        var exists = await rw.FileExistsAsync(dstFile);

        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public async Task GetFileBytesAsync_ShouldGetBytes()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var path = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        Assert.That(path, Does.StartWith(_tenantContext.DotYouRegistryId.ToString()));

        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(path, someBytes);

        await Task.Delay(100);

        var exists = await _s3PayloadStorage.FileExistsAsync(path);
        Assert.That(exists, Is.True);

        var bytes = await rw.GetFileBytesAsync(path);
        Assert.That(bytes, Is.EqualTo(someBytes));
    }

    //

}
