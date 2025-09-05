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
using Odin.Core.Storage.ObjectStorage;
using Odin.Test.Helpers.Secrets;
using Testcontainers.Minio;

namespace Odin.Core.Storage.Tests.ObjectStorage;

#if RUN_S3_TESTS

public class S3AwsStorageTests
{
    private string _accessKey = "";
    private string _secretAccessKey = "";
    private string _bucketName = "";
    private string _testRootPath = "";
    private IAmazonS3 _s3Client = null!;
    private MinioContainer _minioContainer = null!;
    private readonly Mock<ILogger<S3AwsStorage>> _loggerMock = new ();

    [SetUp]
    public async Task SetUp()
    {
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

        _bucketName = $"zzz-ci-test-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(_bucketName);

        _testRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRootPath);
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        // Remove all objects in the bucket and the bucket itself
        await DeleteAllObjectsAsync(_bucketName);
        await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _bucketName });

        // Remove test root path
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, true);
        }

        if (_minioContainer != null)
        {
            await _minioContainer.DisposeAsync();
        }
    }

    //

    // SEB:NOTE this will not delete versioned objects (if versioning is enabled on the bucket).
    private async Task DeleteAllObjectsAsync(string bucketName)
    {
        string continuationToken = null;

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

    [Test]
    public async Task S3AwsStorage_ItShouldCreateABucket()
    {
        var someOtherBucketName = $"zzz-ci-test-{Guid.NewGuid():N}";
        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, someOtherBucketName);
        await bucket.CreateBucketAsync();
        var bucketExists = await bucket.BucketExistsAsync();
        await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = someOtherBucketName });
        Assert.That(bucketExists, Is.True);
    }

    //

    [Test]
    public async Task S3AwsStorage_BucketShouldExist()
    {
        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);
        var bucketExists = await bucket.BucketExistsAsync();
        Assert.That(bucketExists, Is.True);
        Assert.That(bucket.BucketName, Is.EqualTo(_bucketName));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldReadAndWriteFile()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldReadAndWriteFileWithRootPath()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName, "the-root");

        // Write to bucket
        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldReadAndWriteFileWithOffsetAndLength()
    {
        const string path = "the-file";
        var bytes = new byte[]{ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(path, bytes);

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path, 1, 8);
        Assert.That(copy, Is.EqualTo(new byte[]{ 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldReadAndWriteFileWithOffsetAndLengthMaxedOut()
    {
        const string path = "the-file";
        var bytes = new byte[]{ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(path, bytes);

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path, 9, long.MaxValue);
        Assert.That(copy, Is.EqualTo(new byte[]{ 9 }));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldThrowOnBadOffset()
    {
        const string path = "the-file";
        var bytes = new byte[]{ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        await bucket.WriteBytesAsync(path, bytes);

        var exception = Assert.ThrowsAsync<S3StorageException>(() =>  bucket.ReadBytesAsync(path, 10, long.MaxValue));
        var inner = exception!.InnerException as AmazonS3Exception;
        Assert.That(inner!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.RequestedRangeNotSatisfiable));
    }

    //

    [Test]
    public void S3AwsStorage_ItShouldThrowWhenReadingNotExistingPath()
    {
        const string path = "the-file-not-existing";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        var exception = Assert.ThrowsAsync<S3StorageException>(() =>  bucket.ReadBytesAsync(path));
        var inner = exception!.InnerException as AmazonS3Exception;
        Assert.That(inner!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
        Assert.That(inner!.Message, Is.EqualTo("The specified key does not exist."));
    }


    //

    [Test]
    public async Task S3AwsStorage_ItShouldCheckFileExistence()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        var exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.False);

        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public void S3AwsStorage_ItShouldThrowWhenWritingToFolder()
    {
        const string path = "the-file/";
        const string text = "test";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        var result = bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
        Assert.ThrowsAsync<S3StorageException>(async () => await result);
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldDeleteFile()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        await bucket.DeleteFileAsync(path); // should not throw
        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.DeleteFileAsync(path);

        var exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldCopyFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        await bucket.WriteBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.CopyFileAsync(srcPath, dstPath);

        var srcCopy = await bucket.ReadBytesAsync(srcPath);
        Assert.That(srcCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));

        var dstCopy = await bucket.ReadBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldMoveFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        await bucket.WriteBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.MoveFileAsync(srcPath, dstPath);

        var exists = await bucket.FileExistsAsync(srcPath);
        Assert.That(exists, Is.False);

        var dstCopy = await bucket.ReadBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldUploadFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";

        var srcFile = Path.Combine(_testRootPath, srcPath);
        await File.WriteAllTextAsync(srcFile, "Hello");

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);
        await bucket.UploadFileAsync(srcFile, dstPath);

        var exists = await bucket.FileExistsAsync(dstPath);
        Assert.That(exists, Is.True);

        var dstCopy = await bucket.ReadBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo("Hello"));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldDownloadFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "hello";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));

        // Download to local file
        var dstFile = Path.Combine(_testRootPath, dstPath);
        await bucket.DownloadFileAsync(srcPath, dstFile);

        // Check that the file exists
        Assert.That(File.Exists(dstFile), Is.True);

        // Check that the file content is correct
        var content = await File.ReadAllTextAsync(dstFile);
        Assert.That(content, Is.EqualTo(text));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldGetTheFileSize()
    {
        const string srcPath = "the-src-file";
        const string text = "hello";

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));

        // Get the file size
        var fileSize = await bucket.FileLengthAsync(srcPath);
        Assert.That(fileSize, Is.EqualTo(text.Length));
    }

    //

    [Test]
    public void S3AwsStorage_ItShouldThrow_WhenGettingTheFileSize_OfMissingFile()
    {
        var srcPath = Guid.NewGuid().ToString("N");

        var bucket = new S3AwsStorage(_loggerMock.Object, _s3Client, _bucketName);

        var exception = Assert.ThrowsAsync<S3StorageException>(() => bucket.FileLengthAsync(srcPath));
        var inner = exception!.InnerException as AmazonS3Exception;
        Assert.That(inner!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
    }

    //

}

#endif