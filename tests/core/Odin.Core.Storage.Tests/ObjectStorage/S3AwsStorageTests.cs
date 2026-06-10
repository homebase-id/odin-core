using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Odin.Core.Storage.ObjectStorage;
using Odin.Test.Helpers.Logging;
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
    private readonly ILogger<S3AwsStorage> _logger = TestLogFactory.CreateConsoleLogger<S3AwsStorage>();

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
        var bucket = new S3AwsStorage(_logger, _s3Client, someOtherBucketName);
        await bucket.CreateBucketAsync();
        var bucketExists = await bucket.BucketExistsAsync();
        await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = someOtherBucketName });
        Assert.That(bucketExists, Is.True);
    }

    //

    [Test]
    public async Task S3AwsStorage_BucketShouldExist()
    {
        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);
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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        // Write to bucket
        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldReadAndWriteStream()
    {
        const string path = "the-file";
        const string text = "test";
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        // Write to bucket from a stream
        using var stream = new MemoryStream(bytes);
        var bytesWritten = await bucket.WriteStreamAsync(path, stream);
        Assert.That(bytesWritten, Is.EqualTo(bytes.Length));

        // Read back from bucket
        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy.ToStringFromUtf8Bytes(), Is.EqualTo(text));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldWriteEntireStreamRegardlessOfPosition()
    {
        const string path = "the-file";
        var bytes = new byte[]{ 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        // The S3 SDK rewinds the stream, so the whole object is written even when the
        // position is advanced, and the returned count equals the full stream length.
        using var stream = new MemoryStream(bytes);
        stream.Position = 3;
        var bytesWritten = await bucket.WriteStreamAsync(path, stream);
        Assert.That(bytesWritten, Is.EqualTo(bytes.Length));

        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy, Is.EqualTo(bytes));
    }

    //

    [Test]
    public void S3AwsStorage_ItShouldThrowWhenWritingStreamToFolder()
    {
        const string path = "the-file/";

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("test"));
        Assert.ThrowsAsync<S3StorageException>(async () => await bucket.WriteStreamAsync(path, stream));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldReadAndWriteFileWithRootPath()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, "the-root");

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        var result = bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
        Assert.ThrowsAsync<S3StorageException>(async () => await result);
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldDeleteFile()
    {
        const string path = "the-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        await bucket.DeleteFileAsync(path); // should not throw
        await bucket.WriteBytesAsync(path, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.DeleteFileAsync(path);

        var exists = await bucket.FileExistsAsync(path);
        Assert.That(exists, Is.False);
    }

    //

    [Test]
    [TestCase("")]
    [TestCase("payloads")]
    public async Task S3AwsStorage_ItShouldDeleteADirectory(string root)
    {
        const string dir = "the-dir/";
        const string path = dir + "the-file";
        const string text = "test";
        const int fileCount = 10;

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, root);

        await bucket.DeleteDirectoryAsync(dir); // Should not throw

        // Create files
        for (var idx = 0; idx < fileCount; idx++)
        {
            await bucket.WriteBytesAsync(path + idx, System.Text.Encoding.UTF8.GetBytes(text));
        }

        // Check that files exist
        for (var idx = 0; idx < fileCount; idx++)
        {
            var exists = await bucket.FileExistsAsync(path + idx);
            Assert.That(exists, Is.True);
        }

        // Delete directory
        await bucket.DeleteDirectoryAsync(dir);

        // Check that files don't exist anymore
        for (var idx = 0; idx < fileCount; idx++)
        {
            var exists = await bucket.FileExistsAsync(path + idx);
            Assert.That(exists, Is.False);
        }
    }

    //

    [Test]
    [TestCase("")]
    [TestCase("inbox")]
    public async Task S3AwsStorage_ItShouldDeleteByPrefix(string root)
    {
        const string text = "test";
        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, root);

        var driveDir = "drives/aaaa";
        var fileId = "1234567890abcdef";
        var otherFileId = "fedcba0987654321";

        var targetKeys = new[]
        {
            $"{driveDir}/{fileId}.metadata",
            $"{driveDir}/{fileId}.transferkeyheader",
            $"{driveDir}/{fileId}.foo-1.payload",
            $"{driveDir}/{fileId}.foo-1-320x320.thumb",
        };
        var survivorKey = $"{driveDir}/{otherFileId}.foo-2.payload";

        foreach (var k in targetKeys)
            await bucket.WriteBytesAsync(k, System.Text.Encoding.UTF8.GetBytes(text));
        await bucket.WriteBytesAsync(survivorKey, System.Text.Encoding.UTF8.GetBytes(text));

        await bucket.DeleteByPrefixAsync($"{driveDir}/{fileId}.");

        foreach (var k in targetKeys)
            Assert.That(await bucket.FileExistsAsync(k), Is.False, $"{k} should be deleted");
        Assert.That(await bucket.FileExistsAsync(survivorKey), Is.True, "sibling fileId must survive");
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldCopyFile()
    {
        const string srcPath = "the-src-file";
        const string dstPath = "the-dst-file";
        const string text = "test";

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);
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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

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

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        var exception = Assert.ThrowsAsync<S3StorageException>(() => bucket.FileLengthAsync(srcPath));
        var inner = exception!.InnerException as AmazonS3Exception;
        Assert.That(inner!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldReconcileExpirationLifecycle()
    {
        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, "inbox");

        await bucket.EnsureExpirationLifecycleAsync(0);
        var rules = await GetLifecycleRuleDaysAsync(_bucketName);
        Assert.That(rules, Is.Empty, "no expiration rule expected when days = 0");

        await bucket.EnsureExpirationLifecycleAsync(7);
        rules = await GetLifecycleRuleDaysAsync(_bucketName);
        Assert.That(rules, Does.Contain(7));
        Assert.That(await GetRulePrefixAsync(_bucketName, "odin-inbox-expiration"), Is.EqualTo("inbox/"),
            "rule must be scoped to the storage root prefix, not bucket-wide");

        await bucket.EnsureExpirationLifecycleAsync(3);
        rules = await GetLifecycleRuleDaysAsync(_bucketName);
        Assert.That(rules, Does.Contain(3));
        Assert.That(rules, Does.Not.Contain(7));

        await bucket.EnsureExpirationLifecycleAsync(0);
        rules = await GetLifecycleRuleDaysAsync(_bucketName);
        Assert.That(rules, Is.Empty, "rule should be removed when days returns to 0");
    }

    private async Task<List<int>> GetLifecycleRuleDaysAsync(string bucketName)
    {
        try
        {
            var resp = await _s3Client.GetLifecycleConfigurationAsync(
                new GetLifecycleConfigurationRequest { BucketName = bucketName });
            return resp.Configuration?.Rules?
                .Where(r => r.Expiration?.Days != null)
                .Select(r => r.Expiration!.Days!.Value)
                .ToList() ?? new List<int>();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new List<int>();
        }
    }

    private async Task<string> GetRulePrefixAsync(string bucketName, string ruleId)
    {
        var resp = await _s3Client.GetLifecycleConfigurationAsync(
            new GetLifecycleConfigurationRequest { BucketName = bucketName });
        var rule = resp.Configuration?.Rules?.FirstOrDefault(r => r.Id == ruleId);
        return (rule?.Filter?.LifecycleFilterPredicate as LifecyclePrefixPredicate)?.Prefix;
    }

    //

    [Test]
    public async Task S3AwsStorage_ExpirationLifecycle_PreservesForeignRules()
    {
        // Pre-seed a foreign rule directly via the client.
        await _s3Client.PutLifecycleConfigurationAsync(new PutLifecycleConfigurationRequest
        {
            BucketName = _bucketName,
            Configuration = new LifecycleConfiguration
            {
                Rules = new List<LifecycleRule>
                {
                    new LifecycleRule
                    {
                        Id = "foreign-rule",
                        Status = LifecycleRuleStatus.Enabled,
                        Filter = new LifecycleFilter
                        {
                            LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = "other/" }
                        },
                        Expiration = new LifecycleRuleExpiration { Days = 99 }
                    }
                }
            }
        });

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, "inbox");

        // Add our rule
        await bucket.EnsureExpirationLifecycleAsync(5);
        var resp = await _s3Client.GetLifecycleConfigurationAsync(
            new GetLifecycleConfigurationRequest { BucketName = _bucketName });
        var ids = resp.Configuration.Rules.Select(r => r.Id).ToList();
        Assert.That(ids, Does.Contain("foreign-rule"));
        Assert.That(ids, Does.Contain("odin-inbox-expiration"));

        // Remove our rule; foreign rule must survive (config not deleted).
        await bucket.EnsureExpirationLifecycleAsync(0);
        resp = await _s3Client.GetLifecycleConfigurationAsync(
            new GetLifecycleConfigurationRequest { BucketName = _bucketName });
        ids = resp.Configuration.Rules.Select(r => r.Id).ToList();
        Assert.That(ids, Does.Contain("foreign-rule"));
        Assert.That(ids, Does.Not.Contain("odin-inbox-expiration"));
    }

    //

    [Test]
    public async Task S3AwsStorage_ExpirationLifecycle_AppliesBucketWide_WhenNoRootPath()
    {
        // No rootPath -> the rule must use an empty prefix, i.e. apply to the whole bucket.
        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        await bucket.EnsureExpirationLifecycleAsync(7);

        var rules = await GetLifecycleRuleDaysAsync(_bucketName);
        Assert.That(rules, Does.Contain(7));

        var prefix = await GetRulePrefixAsync(_bucketName, "odin-inbox-expiration");
        Assert.That(prefix, Is.Null.Or.Empty, "no rootPath should produce a bucket-wide (empty prefix) rule");
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldWriteNonSeekableStream()
    {
        const string path = "the-file";
        var bytes = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName);

        // A non-seekable stream (e.g. an ASP.NET multipart request section) has no determinable
        // length; WriteStreamAsync must spill it to a temp file so the upload still succeeds.
        using var stream = new NonSeekableStream(bytes);
        var bytesWritten = await bucket.WriteStreamAsync(path, stream);
        Assert.That(bytesWritten, Is.EqualTo(bytes.Length));

        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy, Is.EqualTo(bytes));
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldWriteNonSeekableStream_WithRootPath()
    {
        const string path = "the-file";
        var bytes = new byte[] { 10, 20, 30, 40, 50 };

        // With a root path, the non-seekable spill must still land at the rootPath-prefixed key
        // (and not double-apply the prefix); reading back through the same store proves the key matches.
        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, "the-root");

        using var stream = new NonSeekableStream(bytes);
        var bytesWritten = await bucket.WriteStreamAsync(path, stream);
        Assert.That(bytesWritten, Is.EqualTo(bytes.Length));

        var copy = await bucket.ReadBytesAsync(path);
        Assert.That(copy, Is.EqualTo(bytes));
    }

    //

    // Mimics a forward-only request section (ASP.NET's MultipartReaderStream): readable, not
    // seekable, and Length/Position throw.
    private sealed class NonSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        public NonSeekableStream(byte[] bytes) => _inner = new MemoryStream(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}

#endif