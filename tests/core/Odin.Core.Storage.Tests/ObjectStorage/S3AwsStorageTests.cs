using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
        await CreateBucketAndWaitUntilReadyAsync(_bucketName, waitForConsistency: runTestAgainstHetzner);

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

    // Hetzner Object Storage is eventually consistent on bucket creation: PutBucket returns before the
    // bucket is fully usable, so an object op issued immediately after can fail (writes come back 404,
    // lists throw an opaque HTTP error, and reads can lag writes). The not-ready signal is heterogeneous,
    // so rather than filter on a status code we poll a real write -> read-back -> delete until the whole
    // round-trip succeeds - exactly what the tests do. Once a bucket becomes observable it stays
    // observable, so blocking here in SetUp also covers the same race in the test body and in TearDown.
    // MinIO is strongly consistent and skips the wait.
    private async Task CreateBucketAndWaitUntilReadyAsync(string bucketName, bool waitForConsistency)
    {
        await _s3Client.PutBucketAsync(bucketName);

        if (!waitForConsistency)
        {
            return;
        }

        const string probeKey = "__bucket-ready-probe__";
        var timeout = TimeSpan.FromSeconds(60);
        var pollInterval = TimeSpan.FromSeconds(1);
        var sw = Stopwatch.StartNew();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _s3Client.PutObjectAsync(new PutObjectRequest
                {
                    BucketName = bucketName,
                    Key = probeKey,
                    ContentBody = "ready",
                });

                // Confirm read-after-write, not just write: on a freshly created Hetzner bucket a write
                // can start succeeding a moment before a read of the same key does, and the tests
                // write-then-read (e.g. S3AwsStorage_ItShouldWriteNonSeekableStream_WithRootPath). A 404
                // here is just "not ready yet" and is retried like any other probe failure.
                (await _s3Client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = bucketName,
                    Key = probeKey,
                })).Dispose();

                await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = bucketName,
                    Key = probeKey,
                });

                _logger.LogDebug("Bucket '{Bucket}' ready after {Elapsed}ms ({Attempt} attempt(s))",
                    bucketName, sw.ElapsedMilliseconds, attempt);
                return;
            }
            // The not-ready window is the whole point of this loop, and on Hetzner it surfaces with an
            // unpredictable status: 404, but also 401/403 while the new bucket's ownership/policy
            // propagates, 5xx, or an opaque error carrying no status at all. A transient 401/403 is
            // indistinguishable by code from a permanent auth failure, so we retry every S3 error until
            // the bucket works or the timeout elapses; a genuine misconfig still surfaces (slower) via
            // the TimeoutException, which carries the last status, error code, and message.
            catch (AmazonS3Exception ex)
            {
                if (sw.Elapsed >= timeout)
                {
                    throw new TimeoutException(
                        $"Bucket '{bucketName}' was not ready within {timeout.TotalSeconds:0}s of creation " +
                        $"(last error: status={ex.StatusCode}, code='{ex.ErrorCode}', {ex.Message}).", ex);
                }

                _logger.LogDebug(
                    "Bucket '{Bucket}' not ready yet (attempt {Attempt}, status={Status}, code='{Code}'); retrying in {Delay}ms",
                    bucketName, attempt, ex.StatusCode, ex.ErrorCode, pollInterval.TotalMilliseconds);
                await Task.Delay(pollInterval);
            }
        }
    }

    //

    [Test]
    public async Task S3AwsStorage_ItShouldCreateABucket()
    {
        var someOtherBucketName = $"zzz-ci-test-{Guid.NewGuid():N}";
        var bucket = new S3AwsStorage(_logger, _s3Client, someOtherBucketName);
        // CreateBucketAsync also best-effort installs the abort-incomplete-multipart lifecycle rule.
        // MinIO (the CI backend) rejects bucket-level abort rules by design (it purges stale uploads
        // globally), so the rule won't be stored here -- we only assert that creation still succeeds,
        // i.e. the best-effort path swallows the unsupported-lifecycle error. Rule installation is
        // exercised against providers that support it (AWS/Hetzner/Linode), not against MinIO.
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
    [Explicit("Round-trips an 8GB file (multipart upload + download). Slow and needs ~16GB free disk; run on demand only.")]
    public async Task S3AwsStorage_ItShouldRoundTripALargeMultipartFile()
    {
        const long fileSize = 8L * 1024 * 1024 * 1024; // 8 GiB -> far past the 5GB single-PUT limit, forces multipart
        const int blockSize = 64 * 1024 * 1024;        // 64 MiB; 128 blocks == 8 GiB exactly
        const string key = "big-object.bin";

        var bucket = new S3AwsStorage(_logger, _s3Client, _bucketName, "bigfile");
        var srcPath = Path.Combine(_testRootPath, "big-src.bin");
        var dstPath = Path.Combine(_testRootPath, "big-dst.bin");

        try
        {
            // Write 8 GiB to disk without buffering it in memory, hashing as we go. Each 64 MiB block
            // carries its index in the first 8 bytes so blocks differ (a misassembled object fails the hash).
            var block = new byte[blockSize];
            new Random(0xC0FFEE).NextBytes(block);

            byte[] srcHash;
            using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                await using var fs = new FileStream(
                    srcPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.Asynchronous);
                for (long i = 0; i < fileSize / blockSize; i++)
                {
                    BitConverter.TryWriteBytes(block, i);
                    await fs.WriteAsync(block.AsMemory(0, blockSize));
                    hasher.AppendData(block, 0, blockSize);
                }
                srcHash = hasher.GetHashAndReset();
            }
            Assert.That(new FileInfo(srcPath).Length, Is.EqualTo(fileSize), "source file should be exactly 8 GiB");

            // Upload (multipart via TransferUtility) and download again.
            await bucket.UploadFileAsync(srcPath, key);
            Assert.That(await bucket.FileLengthAsync(key), Is.EqualTo(fileSize), "stored object size must match");

            await bucket.DownloadFileAsync(key, dstPath);
            Assert.That(new FileInfo(dstPath).Length, Is.EqualTo(fileSize), "downloaded file size must match");

            byte[] dstHash;
            await using (var fs = new FileStream(
                             dstPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.Asynchronous))
            {
                dstHash = await SHA256.HashDataAsync(fs);
            }

            Assert.That(Convert.ToHexString(dstHash), Is.EqualTo(Convert.ToHexString(srcHash)),
                "round-tripped 8GB object must be byte-identical");
        }
        finally
        {
            await bucket.DeleteFileAsync(key);
            if (File.Exists(srcPath)) File.Delete(srcPath);
            if (File.Exists(dstPath)) File.Delete(dstPath);
        }
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