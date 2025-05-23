using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Minio;
using Minio.DataModel.Args;
using Moq;
using NUnit.Framework;
using Odin.Core.Storage.ObjectStorage;
using Odin.Test.Helpers.Secrets;

namespace Odin.Core.Storage.Tests.ObjectStorage;

#nullable enable

public class S3StorageTests
{
    private string _accessKey = "";
    private string _secretAccessKey = "";
    private string _bucketName = "";
    private readonly Mock<ILogger<S3Storage>> _loggerMock = new ();
    private string _testRootPath = "";
    private IMinioClient _minioClient = null!;

    //

    [OneTimeSetUp]
    public void CheckCredentials()
    {
        TestSecrets.Load();

        _accessKey = Environment.GetEnvironmentVariable("ODIN_S3_ACCESS_KEY")!;
        _secretAccessKey = Environment.GetEnvironmentVariable("ODIN_S3_SECRET_ACCESS_KEY")!;

        if (string.IsNullOrWhiteSpace(_accessKey) || string.IsNullOrWhiteSpace(_secretAccessKey))
        {
            Assert.Ignore("Environment variable ODIN_S3_ACCESS_KEY or ODIN_S3_SECRET_ACCESS_KEY is not set");
        }
    }

    //

    [SetUp]
    public async Task SetUp()
    {
        _minioClient = new MinioClient()
            .WithEndpoint("hel1.your-objectstorage.com")
            .WithCredentials(_accessKey, _secretAccessKey)
            .WithRegion("hel1")
            .WithSSL()
            .Build();

        _bucketName = $"zzz-ci-test-{Guid.NewGuid():N}";
        await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));

        _testRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_testRootPath);
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        // Remove all objects
        var listArgs = new ListObjectsArgs().WithBucket(_bucketName).WithRecursive(true);
        await foreach (var item in _minioClient.ListObjectsEnumAsync(listArgs))
        {
            await _minioClient.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_bucketName)
                .WithObject(item.Key));
        }

        // Remove bucket
        await _minioClient.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(_bucketName));

        // Remove test root path
        if (Directory.Exists(_testRootPath))
        {
            Directory.Delete(_testRootPath, true);
        }
    }

    //

    [Test]
    public async Task BucketShouldExist()
    {
        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);
        var bucketExists = await bucket.BucketExistsAsync();
        Assert.That(bucketExists, Is.True);
        Assert.That(bucket.BucketName, Is.EqualTo(_bucketName));
    }

    //

    [Test]
    public async Task ItShouldReadAndWriteFile()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        // Test that file exists at the right place through minio client
        var exists = await _minioClient.StatObjectAsync(
            new StatObjectArgs().WithBucket(_bucketName).WithObject(path));
        Assert.That(exists, Is.Not.Null);

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task ItShouldReadAndWriteFileWithOffsetAndLength()
    {
        const string path = "the-file";
        var bytes = new byte[]{ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(path, bytes);

        // Test that file exists at the right place through minio client
        var exists = await _minioClient.StatObjectAsync(
            new StatObjectArgs().WithBucket(_bucketName).WithObject(path));
        Assert.That(exists, Is.Not.Null);

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path, 1, 8);
        Assert.That(copy, Is.EqualTo(new byte[]{ 1, 2, 3, 4, 5, 6, 7, 8 }));
    }

    //

    [Test]
    public async Task ItShouldReadAndWriteFileWithOffsetAndLengthMaxedOut()
    {
        const string path = "the-file";
        var bytes = new byte[]{ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(path, bytes);

        // Test that file exists at the right place through minio client
        var exists = await _minioClient.StatObjectAsync(
            new StatObjectArgs().WithBucket(_bucketName).WithObject(path));
        Assert.That(exists, Is.Not.Null);

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path, 9, long.MaxValue);
        Assert.That(copy, Is.EqualTo(new byte[]{ 9 }));
    }

    //

    [Test]
    public async Task ItShouldThrowOnBadOffset()
    {
        const string path = "the-file";
        var bytes = new byte[]{ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        await bucket.WriteBytesAsync(path, bytes);

        var exists = await _minioClient.StatObjectAsync(
            new StatObjectArgs().WithBucket(_bucketName).WithObject(path));
        Assert.That(exists, Is.Not.Null);

        var exception = Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>  bucket.ReadBytesAsync(path, 10, long.MaxValue));
        Assert.That(exception!.Message, Is.EqualTo("Offset is greater than the size of the object (Parameter 'offset')"));
    }

    //

    [Test]
    public void ItShouldThrowWhenWritingToFolder()
    {
        const string path = "the-file/";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        var result = bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
        Assert.ThrowsAsync<S3StorageException>(async () => await result);
    }

    //

    [Test]
    public async Task ItShouldCheckFileExistence()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        var exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.False);

        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public async Task ItShouldDeleteFile()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        await bucket.DeleteFileAsync(path); // should not throw
        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.DeleteFileAsync(path);

        var exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    public async Task ItShouldCopyFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        await bucket.WriteBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.CopyFileAsync(srcPath, dstPath);

        var srcCopy = await bucket.ReadBytesAsync(srcPath);
        Assert.That(srcCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));

        var dstCopy = await bucket.ReadBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));

    }

    //

    [Test]
    public async Task ItShouldMoveFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        await bucket.WriteBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.MoveFileAsync(srcPath, dstPath);

        var exists = await bucket.FileExistsAsync(srcPath);
        Assert.That(exists, Is.False);

        var dstCopy = await bucket.ReadBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task ItShouldListFiles()
    {
        const string file0 = "file0";
        const string file1 = "parent/file1";
        const string file2 = "parent/file2";
        const string file3 = "parent/child/file3";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

        await bucket.WriteBytesAsync(file0, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.WriteBytesAsync(file1, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.WriteBytesAsync(file2, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.WriteBytesAsync(file3, System.Text.Encoding.UTF8.GetBytes(text));

        {
            var files = bucket.ListFilesAsync("", false);
            Assert.ThrowsAsync<S3StorageException>(async () => await files);
        }

        {
            var files = bucket.ListFilesAsync("", true);
            Assert.ThrowsAsync<S3StorageException>(async () => await files);
        }

        {
            var files = await bucket.ListFilesAsync("/", false);
            Assert.That(files, Has.Count.EqualTo(1));
            Assert.That(files, Has.Member(file0));
        }

        {
            var files = await bucket.ListFilesAsync("/", true);
            Assert.That(files, Has.Count.EqualTo(4));
            Assert.That(files, Has.Member(file0));
            Assert.That(files, Has.Member(file1));
            Assert.That(files, Has.Member(file2));
            Assert.That(files, Has.Member(file3));
        }

        {
            var files = await bucket.ListFilesAsync("/parent/", false);
            Assert.That(files, Has.Count.EqualTo(2));
            Assert.That(files, Has.Member(file1));
            Assert.That(files, Has.Member(file2));
        }

        {
            var files = await bucket.ListFilesAsync("/parent/", true);
            Assert.That(files, Has.Count.EqualTo(3));
            Assert.That(files, Has.Member(file1));
            Assert.That(files, Has.Member(file2));
            Assert.That(files, Has.Member(file3));
        }

        {
            var files = await bucket.ListFilesAsync("parent/", false);
            Assert.That(files, Has.Count.EqualTo(2));
            Assert.That(files, Has.Member(file1));
            Assert.That(files, Has.Member(file2));
        }

        {
            var files = await bucket.ListFilesAsync("parent/", true);
            Assert.That(files, Has.Count.EqualTo(3));
            Assert.That(files, Has.Member(file1));
            Assert.That(files, Has.Member(file2));
            Assert.That(files, Has.Member(file3));
        }

        {
            var files = bucket.ListFilesAsync("/parent", false);
            Assert.ThrowsAsync<S3StorageException>(async () => await files);
        }

        {
            var files = bucket.ListFilesAsync("/parent", true);
            Assert.ThrowsAsync<S3StorageException>(async () => await files);
        }

        {
            var files = await bucket.ListFilesAsync("/notthere/", true);
            Assert.That(files, Has.Count.EqualTo(0));
        }

        {
            var files = await bucket.ListFilesAsync("/notthere/", false);
            Assert.That(files, Has.Count.EqualTo(0));
        }

        {
            var files = await bucket.ListFilesAsync("notthere/", true);
            Assert.That(files, Has.Count.EqualTo(0));
        }

        {
            var files = await bucket.ListFilesAsync("notthere/", false);
            Assert.That(files, Has.Count.EqualTo(0));
        }

        {
            var files = await bucket.ListFilesAsync("/parent/child/", false);
            Assert.That(files, Has.Count.EqualTo(1));
            Assert.That(files, Has.Member(file3));
        }

        {
            var files = await bucket.ListFilesAsync("/parent/child/", true);
            Assert.That(files, Has.Count.EqualTo(1));
            Assert.That(files, Has.Member(file3));
        }

        {
            var files = bucket.ListFilesAsync("/parent/child/file3", true);
            Assert.ThrowsAsync<S3StorageException>(async () => await files);
        }

        {
            var files = await bucket.ListFilesAsync("/parent/child/file3/", true);
            Assert.That(files, Has.Count.EqualTo(0));
        }
    }

    //

    [Test]
    public async Task ItShouldUploadFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";

        var srcFile = Path.Combine(_testRootPath, srcPath);
        await File.WriteAllTextAsync(srcFile, "Hello");

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);
        await bucket.UploadFileAsync(srcFile, dstPath);

        var exists = await bucket.FileExistsAsync(dstPath);
        Assert.That(exists, Is.True);

        var dstCopy = await bucket.ReadBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo("Hello"));
    }

    //

    [Test]
    public async Task ItShouldDownloadFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "hello";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName);

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


}