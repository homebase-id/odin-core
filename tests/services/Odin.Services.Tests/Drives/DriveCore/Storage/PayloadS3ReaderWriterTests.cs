using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;
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
            S3ObjectStorage = new OdinConfiguration.S3ObjectStorageSection
            {
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
            .WithEndpoint(_config.S3ObjectStorage.Endpoint)
            .WithCredentials(_config.S3ObjectStorage.AccessKey, _config.S3ObjectStorage.SecretAccessKey)
            .WithRegion(_config.S3ObjectStorage.Region)
            .WithSSL()
            .Build();

        var bucketName = _config.S3ObjectStorage.BucketName;
        await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(bucketName));

        _s3PayloadStorage = new S3PayloadStorage(
            new Mock<ILogger<S3PayloadStorage>>().Object,
            _minioClient,
            bucketName,
            TenantPathManager.PayloadsFolder);
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            if (_minioClient != null!)
            {
                await _minioClient.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(_config.S3ObjectStorage.BucketName));
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
    public void GetRelativePath_ShouldReturnCorrectPath()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.ZeroTime;
        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var absolutePath = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        Assert.That(absolutePath, Does.StartWith(Path.Combine(_config.Host.TenantDataRootPath, "payloads")));

        var relativePath = rw.GetRelativeS3Path(absolutePath);

        var (high, low) = GuidHelper.GetLastTwoNibbles(fileId);
        Assert.That(relativePath, Is.EqualTo(Path.Combine(
            _tenantContext.DotYouRegistryId.ToString(),
            "drives",
            driveId.ToString("N"),
            "files",
            high.ToString(),
            low.ToString(),
            $"{fileId:N}-{appKey.ToLower()}-0.payload")));
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

        var absolutePath = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(absolutePath, someBytes);

        await Task.Delay(100);

        var relativePath = rw.GetRelativeS3Path(absolutePath);
        var exists = await _s3PayloadStorage.FileExistsAsync(relativePath);
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

        var absolutePath = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(absolutePath, someBytes);

        await Task.Delay(100);

        var relativePath = rw.GetRelativeS3Path(absolutePath);
        var exists = await _s3PayloadStorage.FileExistsAsync(relativePath);
        Assert.That(exists, Is.True);

        await rw.DeleteFileAsync(absolutePath);

        await Task.Delay(100);

        exists = await _s3PayloadStorage.FileExistsAsync(relativePath);
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

        var absolutePath = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(absolutePath, someBytes);

        await Task.Delay(100);

        var exists = await rw.FileExistsAsync(absolutePath);
        Assert.That(exists, Is.True);

        await rw.DeleteFileAsync(absolutePath);
        await Task.Delay(100);

        exists = await rw.FileExistsAsync(absolutePath);
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
        var someBytes = "hello".ToUtf8ByteArray();
        await rw.WriteFileAsync(srcPath, someBytes);

        await Task.Delay(100);

        var exists = await rw.FileExistsAsync(srcPath);
        Assert.That(exists, Is.True);

        var otherDriveId = Guid.NewGuid();
        var dstPath = _tenantPathManager.GetPayloadDirectoryAndFileName(otherDriveId, fileId, appKey, timestamp);
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
    public async Task GetFilesInDirectoryAsync_ShouldGetFilesInDirectory_WithoutFileMask()
    {
        var root = Path.Combine(_tenantPathManager.RootPayloadsPath, "frodo/");

        await CreateFileAsync(Path.Combine(root, "file1.foo"));
        await CreateFileAsync(Path.Combine(root, "file2.bar"));
        await CreateFileAsync(Path.Combine(root, "subdir", "file3.foo"));

        await Task.Delay(100);

        var rw = new PayloadS3ReaderWriter(_tenantContext, _s3PayloadStorage);

        var files = await rw.GetFilesInDirectoryAsync(root);
        Assert.That(files.Length, Is.EqualTo(2));
        Assert.That(files, Does.Contain(Path.Combine(root, "file1.foo")));
        Assert.That(files, Does.Contain(Path.Combine(root, "file2.bar")));
    }

    //

    // [Test]
    // public async Task GetFilesInDirectoryAsync_ShouldGetFilesInDirectory_WithFileMask()
    // {
    //     CreateFile(Path.Combine(TestRootPath, "dir1", "file1.foo"));
    //     CreateFile(Path.Combine(TestRootPath, "dir1", "file2.bar"));
    //     CreateFile(Path.Combine(TestRootPath, "dir1", "dir2", "file3.foo"));
    //
    //     var rw = new PayloadFileReaderWriter(_loggerMock.Object, _tenantContext, _fileReaderWriter);
    //
    //     var files = await rw.GetFilesInDirectoryAsync(Path.Combine(TestRootPath, "dir1"), "*.foo");
    //     Assert.That(files.Length, Is.EqualTo(1));
    //     Assert.That(files, Does.Contain(Path.Combine(TestRootPath, "dir1", "file1.foo")));
    // }

    //





}