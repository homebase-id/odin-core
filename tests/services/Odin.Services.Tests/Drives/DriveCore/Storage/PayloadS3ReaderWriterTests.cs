using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
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
using Testcontainers.Minio;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

#if RUN_S3_TESTS

public class PayloadS3ReaderWriterTests : PayloadReaderWriterBaseTestFixture
{
    private string _accessKey = "";
    private string _secretAccessKey = "";
    private string _bucketName = "";
    private OdinConfiguration _config = null!;
    private TenantContext _tenantContext = null!;
    private TenantPathManager _tenantPathManager = null!;
    private IAmazonS3 _s3Client = null!;
    private IS3PayloadStorage _s3PayloadStorage = null!;
    private MinioContainer _minioContainer = null!;
    private readonly Mock<ILogger<S3AwsPayloadStorage>> _loggerMock = new ();

    //

    [SetUp]
    public async Task Setup()
    {
        BaseSetup();
        TestSecrets.Load();

        var runTestAgainstHetzner = Environment.GetEnvironmentVariable("ODIN_S3_RUN_HETZNER_TESTS")?.ToLower() == "true";
        if (runTestAgainstHetzner)
        {
            _accessKey = Environment.GetEnvironmentVariable("ODIN_S3_ACCESS_KEY")!;
            _secretAccessKey = Environment.GetEnvironmentVariable("ODIN_S3_SECRET_ACCESS_KEY")!;

            _s3Client = new AmazonS3Client(
                _accessKey,
                _secretAccessKey,
                new AmazonS3Config
                {
                    ServiceURL = "https://hel1.your-objectstorage.com",
                    AuthenticationRegion = "hel1",
                    ForcePathStyle = false,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
                });
        }
        else
        {
            _minioContainer = new MinioBuilder()
                .WithImage("minio/minio:RELEASE.2025-05-24T17-08-30Z")
                .WithUsername("minioadmin")
                .WithPassword("minioadmin123")
                .Build();

            await _minioContainer.StartAsync();

            _accessKey = _minioContainer.GetAccessKey();
            _secretAccessKey = _minioContainer.GetSecretKey();

            _s3Client = new AmazonS3Client(
                _accessKey,
                _secretAccessKey,
                new AmazonS3Config
                {
                    ServiceURL = _minioContainer.GetConnectionString(),
                    AuthenticationRegion = "foo",
                    ForcePathStyle = true,
                    ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                    RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
                });
        }

        // Config needed by TenantPathManager to tweak the path for S3 storage
        _config = new OdinConfiguration
        {
            Host = new OdinConfiguration.HostSection
            {
                TenantDataRootPath = Path.Combine(TestRootPath, "tenants"),
            },
            S3PayloadStorage = new OdinConfiguration.S3PayloadStorageSection
            {
                Enabled = true,
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
            markedForDeletionDate: null,
            email: ""
        );
        _tenantPathManager = _tenantContext.TenantPathManager;

        _bucketName = $"zz-ci-test-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(_bucketName);

        _s3PayloadStorage = new S3AwsPayloadStorage(_loggerMock.Object, _s3Client, _bucketName);
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            // Remove all objects in the bucket and the bucket itself
            await DeleteAllObjectsAsync(_bucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _bucketName });

        }
        finally
        {
            BaseTearDown();
        }
    }

    //

    // SEB:NOTE this will not delete versioned objects (if versioning is enabled on the bucket).
    private async Task DeleteAllObjectsAsync(string bucketName)
    {
        string? continuationToken = null;

        do
        {
            var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName            = bucketName,
                ContinuationToken     = continuationToken,
                MaxKeys               = 1000
            });

            if (listResponse.S3Objects is { Count: > 0 })
            {
                var deleteRequest = new DeleteObjectsRequest
                {
                    BucketName = bucketName,
                    Objects    = listResponse.S3Objects
                        .Select(o => new KeyVersion { Key = o.Key })
                        .ToList()
                };

                await _s3Client.DeleteObjectsAsync(deleteRequest);
            }

            continuationToken = listResponse.NextContinuationToken;
        }
        while (continuationToken != null);
    }

    //

    private async Task CreateFileAsync(string filePath, string content = "hello")
    {
        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);
        var someBytes = content.ToUtf8ByteArray();
        await rw.WriteFileAsync(filePath, someBytes);
    }

    //

    [Test]
    public async Task PayloadS3_WriteFileAsync_ShouldWriteFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);

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
    public async Task PayloadS3_DeleteFileAsync_ShouldDeleteFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);

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
    public async Task PayloadS3_FileExistsAsync_ShouldCheckIfFileExists()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);

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
    public async Task PayloadS3_MoveFileAsync_ShouldMoveTheFile()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);

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
    public Task PayloadS3_MoveFileAsync_ShouldThrowOnMissingSrcFile()
    {
        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);

        var srcFile = Path.Combine(TestRootPath, Guid.NewGuid().ToString());
        var dstFile = Path.Combine(TestRootPath, Guid.NewGuid().ToString());

        Assert.ThrowsAsync<PayloadReaderWriterException>(() => rw.MoveFileAsync(srcFile, dstFile));

        return Task.CompletedTask;
    }

    //

    [Test]
    public async Task PayloadS3_CreateDirectoryAsync_ShouldCreateDirectory()
    {
        var root = Path.Combine(_tenantPathManager.RootPayloadsPath, "frodo/sam");
        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);
        await rw.CreateDirectoryAsync(root);
        Assert.Pass(); // No-op: S3 does not have directories in the same way as a file system.
    }

    //

    [Test]
    public async Task PayloadS3_CopyPayloadFileAsync_ShouldCopyFileToPayloads()
    {
        var srcFile = Path.Combine(TestRootPath, "file1.foo");
        CreateFile(srcFile);

        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var dstFile = _tenantPathManager.GetPayloadDirectoryAndFileName(driveId, fileId, appKey, timestamp);

        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);

        await rw.CopyPayloadFileAsync(srcFile, dstFile);
        var exists = await rw.FileExistsAsync(dstFile);

        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public async Task PayloadS3_GetFileBytesAsync_ShouldGetBytes()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var appKey = "testAppKey";
        var timestamp = UnixTimeUtcUnique.Now();

        var rw = new PayloadS3ReaderWriter(_s3PayloadStorage);

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

#endif