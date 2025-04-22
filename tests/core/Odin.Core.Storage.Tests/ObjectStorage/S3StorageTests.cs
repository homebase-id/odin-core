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

public class S3StorageBucketTests
{
    private const string RootPath = "the-root-path";
    private string _bucketName = "";
    private readonly Mock<ILogger<S3Storage>> _loggerMock = new ();
    private IMinioClient _minioClient = null!;

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

        _minioClient = new MinioClient()
            .WithEndpoint("hel1.your-objectstorage.com")
            .WithCredentials(accessKey, secretAccessKey)
            .WithRegion("hel1")
            .WithSSL()
            .Build();

        _bucketName = $"zz-ci-test-{Guid.NewGuid():N}";
        await _minioClient.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucketName));
    }

    //

    [TearDown]
    public async Task TearDown()
    {
        if (_minioClient != null!)
        {
            await _minioClient.RemoveBucketAsync(new RemoveBucketArgs().WithBucket(_bucketName));
        }
    }

    //

    [Test]
    public async Task BucketShouldExist()
    {
        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);
        var bucketExists = await bucket.BucketExistsAsync();
        Assert.That(bucketExists, Is.True);
        Assert.That(bucket.BucketName, Is.EqualTo(_bucketName));
        Assert.That(bucket.RootPath, Is.EqualTo(RootPath));
    }

    //

    [Test]
    public async Task ItShouldReadAndWriteFile()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);

        // Write to bucket
        await bucket.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        // Test that file exists at the right place through minio client
        var exists = await _minioClient.StatObjectAsync(
            new StatObjectArgs().WithBucket(_bucketName).WithObject(S3Path.Combine(RootPath, path)));
        Assert.That(exists, Is.Not.Null);

        // Read back from bucket
        var copy = await bucket.ReadAllBytesAsync(path);
        Assert.That(copy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public void ItShouldThrowWhenWritingToFolder()
    {
        const string path = "the-file/";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);

        var result = bucket.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
        Assert.ThrowsAsync<S3StorageException>(async () => await result);
    }

    //

    [Test]
    public async Task ItShouldCheckFileExistence()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);

        var exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.False);

        await bucket.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.True);
    }

    //

    [Test]
    public async Task ItShouldDeleteFile()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);

        await bucket.DeleteFileAsync(path); // should not throw
        await bucket.WriteAllBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
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

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);

        await bucket.WriteAllBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.CopyFileAsync(srcPath, dstPath);

        var srcCopy = await bucket.ReadAllBytesAsync(srcPath);
        Assert.That(srcCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));

        var dstCopy = await bucket.ReadAllBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));

    }

    //

    [Test]
    public async Task ItShouldMoveFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);

        await bucket.WriteAllBytesAsync(srcPath, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.MoveFileAsync(srcPath, dstPath);

        var exists = await bucket.FileExistsAsync(srcPath);
        Assert.That(exists, Is.False);

        var dstCopy = await bucket.ReadAllBytesAsync(dstPath);
        Assert.That(dstCopy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task ItShouldListFiles()
    {
        const string file0 = "/file0";
        const string file1 = "/parent/file1";
        const string file2 = "/parent/file2";
        const string file3 = "/parent/child/file3";
        const string text = "test";

        var bucket = new S3Storage(_loggerMock.Object, _minioClient, _bucketName, RootPath);

        await bucket.WriteAllBytesAsync(file0, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.WriteAllBytesAsync(file1, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.WriteAllBytesAsync(file2, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.WriteAllBytesAsync(file3, System.Text.Encoding.UTF8.GetBytes(text));

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
}