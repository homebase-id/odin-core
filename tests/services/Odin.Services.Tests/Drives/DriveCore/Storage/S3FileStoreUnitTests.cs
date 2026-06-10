using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.Runtime.Internal;
using Amazon.Runtime.Internal.Transform;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Storage.ObjectStorage;
using Odin.Core.Storage.Database.Identity.Connection;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Core.Tasks;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Test.Helpers.Logging;
using Odin.Test.Helpers.Secrets;
using Testcontainers.Minio;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

// Pure unit tests for S3FileStore's own logic (retry classification, exception translation,
// cancellation passthrough, delegation). These mock IS3Storage, so they need neither Minio
// nor RUN_S3_TESTS and run on every build. The Minio round-trip behaviour lives in
// S3FileStoreTests below (gated behind RUN_S3_TESTS).
public class S3FileStoreUnitTests
{
    // Fast retry config: real prod defaults are 5 attempts / 5s backoff. Keep enough
    // attempts (>=2) for the retry-classification probes to exercise a retry, but ~zero
    // backoff so a genuine retry doesn't add wall-clock time.
    private static OdinConfiguration Config(int attempts = 5, int backoffMs = 1) =>
        new()
        {
            S3Storage = new OdinConfiguration.S3StorageSection
            {
                RetryAttempts = attempts,
                RetryInitialBackoffMs = backoffMs
            }
        };

    private static S3FileStore Sut(IS3Storage storage, OdinConfiguration? config = null) =>
        new(storage, new Mock<ILogger<S3FileStore>>().Object, config ?? Config());

    // Mirrors how S3AwsStorage surfaces failures: an S3StorageException wrapping the AmazonS3Exception.
    private static S3StorageException S3Failure(HttpStatusCode status) =>
        new("wrapped s3 error", new AmazonS3Exception("boom") { StatusCode = status });

    private static S3StorageException S3Timeout() =>
        new("wrapped timeout", new AmazonS3Exception("the request did not respond in time"));

    private static S3StorageException S3InnerHttp5xx()
    {
        var response = new Mock<IWebResponseData>();
        response.SetupGet(r => r.StatusCode).Returns(HttpStatusCode.BadGateway);
        // Outer status is deliberately non-5xx so only the inner-http branch can classify this as retryable.
        var s3 = new AmazonS3Exception("transport failure", new HttpErrorResponseException(response.Object))
        {
            StatusCode = HttpStatusCode.OK
        };
        return new S3StorageException("wrapped inner-5xx", s3);
    }

    private static async Task<Exception?> Capture(Func<Task> act)
    {
        try
        {
            await act();
            return null;
        }
        catch (Exception e)
        {
            return e;
        }
    }

    // Proves the retry predicate's decision WITHOUT sleeping through the configured backoff.
    // The storage call cancels the token the instant it is invoked, then throws `failure`:
    //   - retryable     -> execution advances to the cancellable backoff delay (already cancelled) -> OCE
    //   - non-retryable -> DriveFileStoreException is thrown immediately, never reaching the delay
    // Either way the operation is attempted exactly once, so no real retry/backoff time is spent.
    private static async Task AssertRetryable(Exception failure)
    {
        using var cts = new CancellationTokenSource();
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).ReadAllBytesAsync("d/drives/f.metadata", cts.Token));

        Assert.That(caught, Is.InstanceOf<OperationCanceledException>(),
            "a retryable failure must advance into the (cancelled) backoff delay");
        storage.Verify(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertNotRetryable(Exception failure)
    {
        using var cts = new CancellationTokenSource();
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).ReadAllBytesAsync("d/drives/f.metadata", cts.Token));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>(),
            "a non-retryable failure must surface immediately, even with the token cancelled");
        Assert.That(caught!.InnerException, Is.SameAs(failure));
        storage.Verify(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- backend type ---

    [Test]
    public void Backend_Is_S3()
    {
        var storage = new Mock<IS3Storage>();
        Assert.That(Sut(storage.Object).Backend, Is.EqualTo(StorageBackendType.S3));
    }

    // --- retry classification (the predicate in CreateRetry) ---

    [Test]
    public Task Retries_On_Outer_5xx() => AssertRetryable(S3Failure(HttpStatusCode.InternalServerError));

    [Test]
    public Task Retries_On_Outer_502() => AssertRetryable(S3Failure(HttpStatusCode.BadGateway));

    [Test]
    public Task Retries_On_Timeout_Message() => AssertRetryable(S3Timeout());

    [Test]
    public Task Retries_On_Inner_Http_5xx() => AssertRetryable(S3InnerHttp5xx());

    [Test]
    public Task Does_Not_Retry_On_404() => AssertNotRetryable(S3Failure(HttpStatusCode.NotFound));

    [Test]
    public Task Does_Not_Retry_On_403() => AssertNotRetryable(S3Failure(HttpStatusCode.Forbidden));

    [Test]
    public Task Does_Not_Retry_When_Inner_Not_AmazonS3() =>
        AssertNotRetryable(new S3StorageException("x", new InvalidOperationException("y")));

    [Test]
    public Task Does_Not_Retry_When_No_Inner() =>
        AssertNotRetryable(new S3StorageException("no inner at all"));

    // --- retry actually happens and config drives it ---

    [Test]
    public async Task Retries_Then_Succeeds_On_Transient_5xx()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var storage = new Mock<IS3Storage>();
        storage.SetupSequence(x => x.ReadBytesAsync("p", It.IsAny<CancellationToken>()))
            .ThrowsAsync(S3Failure(HttpStatusCode.ServiceUnavailable))
            .ReturnsAsync(bytes);

        var result = await Sut(storage.Object).ReadAllBytesAsync("p");

        Assert.That(result, Is.EqualTo(bytes));
        storage.Verify(x => x.ReadBytesAsync("p", It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Test]
    public async Task RetryAttempts_Config_Is_Honored()
    {
        var failure = S3Failure(HttpStatusCode.ServiceUnavailable);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync("p", It.IsAny<CancellationToken>())).ThrowsAsync(failure);

        // attempts=1 -> even a normally-retryable 5xx is attempted exactly once, then wrapped.
        var caught = await Capture(() => Sut(storage.Object, Config(attempts: 1)).ReadAllBytesAsync("p"));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        storage.Verify(x => x.ReadBytesAsync("p", It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- cancellation passthrough ---

    [Test]
    public async Task Propagates_Cancellation_Unwrapped()
    {
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var caught = await Capture(() => Sut(storage.Object).ReadAllBytesAsync("p"));

        Assert.That(caught, Is.InstanceOf<OperationCanceledException>());
        Assert.That(caught, Is.Not.InstanceOf<DriveFileStoreException>());
        // OCE is never retried.
        storage.Verify(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- exception translation per method ---

    [Test]
    public async Task WriteStream_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.WriteStreamAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        using var ms = new MemoryStream(new byte[] { 1, 2, 3 });
        var caught = await Capture(() => Sut(storage.Object).WriteStreamAsync("p", ms));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public async Task WriteBytes_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.WriteBytesAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).WriteBytesAsync("p", new byte[] { 1 }));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public async Task ReadAllBytes_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).ReadAllBytesAsync("p"));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public async Task ReadBytes_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync(It.IsAny<string>(), It.IsAny<long>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).ReadBytesAsync("p", 0, 10));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public async Task Exists_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.FileExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).ExistsAsync("p"));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public async Task Length_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.FileLengthAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).LengthAsync("p"));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public async Task Delete_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.DeleteFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).DeleteAsync("p"));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    [Test]
    public async Task DeleteSet_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.DeleteByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var caught = await Capture(() => Sut(storage.Object).DeleteSetAsync("d", Guid.NewGuid()));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    // --- delegation / mapping ---

    [Test]
    public async Task WriteStream_Returns_BytesWritten_As_UInt()
    {
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.WriteStreamAsync("path", It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(123L);

        using var ms = new MemoryStream();
        var written = await Sut(storage.Object).WriteStreamAsync("path", ms);

        Assert.That(written, Is.EqualTo(123u));
    }

    [Test]
    public async Task WriteBytes_Delegates_To_S3_WriteBytesAsync()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.WriteBytesAsync("path", bytes, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await Sut(storage.Object).WriteBytesAsync("path", bytes);

        storage.Verify(x => x.WriteBytesAsync("path", bytes, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ReadAllBytes_Returns_Underlying_Bytes()
    {
        var bytes = new byte[] { 9, 8, 7 };
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync("p", It.IsAny<CancellationToken>())).ReturnsAsync(bytes);

        Assert.That(await Sut(storage.Object).ReadAllBytesAsync("p"), Is.EqualTo(bytes));
    }

    [Test]
    public async Task ReadBytes_Delegates_With_Offset_And_Length()
    {
        var bytes = new byte[] { 4, 5, 6 };
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.ReadBytesAsync("p", 10L, 20L, It.IsAny<CancellationToken>())).ReturnsAsync(bytes);

        Assert.That(await Sut(storage.Object).ReadBytesAsync("p", 10, 20), Is.EqualTo(bytes));
        storage.Verify(x => x.ReadBytesAsync("p", 10L, 20L, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task Exists_Returns_Underlying_Result()
    {
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.FileExistsAsync("p", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        Assert.That(await Sut(storage.Object).ExistsAsync("p"), Is.True);
    }

    [Test]
    public async Task Length_Returns_Underlying_Result()
    {
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.FileLengthAsync("p", It.IsAny<CancellationToken>())).ReturnsAsync(42L);

        Assert.That(await Sut(storage.Object).LengthAsync("p"), Is.EqualTo(42L));
    }

    [Test]
    public async Task Delete_Delegates_To_S3_DeleteFileAsync()
    {
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.DeleteFileAsync("p", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await Sut(storage.Object).DeleteAsync("p");

        storage.Verify(x => x.DeleteFileAsync("p", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task DeleteSet_Deletes_By_FileId_Prefix()
    {
        string? captured = null;
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.DeleteByPrefixAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((p, _) => captured = p)
            .Returns(Task.CompletedTask);

        var fileId = Guid.NewGuid();
        await Sut(storage.Object).DeleteSetAsync("ident/drives/abc", fileId);

        Assert.That(captured, Is.EqualTo($"ident/drives/abc/{fileId:N}."));
    }

    [Test]
    public async Task EnsureDirectory_Is_NoOp_And_Touches_No_Storage()
    {
        var storage = new Mock<IS3Storage>(MockBehavior.Strict);
        await Sut(storage.Object).EnsureDirectoryAsync("any/dir");
        storage.VerifyNoOtherCalls();
    }

    // --- IngestFromAsync ---

    // A minimal fake IDriveFileStore whose only purpose is to report a Backend value.
    private sealed class FakeStore(StorageBackendType backend, (string bucket, string fullKey)? s3Location = null) : IDriveFileStore
    {
        public StorageBackendType Backend => backend;
        public Task<uint> WriteStreamAsync(string p, Stream s, CancellationToken ct = default) => throw new NotImplementedException();
        public Task WriteBytesAsync(string p, byte[] b, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<byte[]> ReadAllBytesAsync(string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<byte[]> ReadBytesAsync(string p, long start, long length, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<bool> ExistsAsync(string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> LengthAsync(string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(string p, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteSetAsync(string d, Guid fileId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task EnsureDirectoryAsync(string d, CancellationToken ct = default) => throw new NotImplementedException();
        public Task IngestFromAsync(IDriveFileStore source, string src, string dst, CancellationToken ct = default) => throw new NotImplementedException();
        public (string bucket, string fullKey)? GetS3Location(string relativePath) => s3Location;
    }

    [Test]
    public async Task IngestFrom_Disk_Source_Calls_UploadFileAsync()
    {
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var diskSource = new FakeStore(StorageBackendType.Disk);
        await Sut(storage.Object).IngestFromAsync(diskSource, "/tmp/src.bin", "dest/key.bin");

        storage.Verify(x => x.UploadFileAsync("/tmp/src.bin", "dest/key.bin", It.IsAny<CancellationToken>()), Times.Once);
    }

    // --- S3 -> S3 cross-bucket ingest ---

    [Test]
    public async Task IngestFrom_S3_Source_Calls_CopyFromBucketAsync_With_Full_Source_Key()
    {
        // The source store is in bucket "source-bucket" with rootPath "inbox".
        // The relative path is "drives/xyz/<fileId>.payload".
        // GetS3Location must return the FULL key: "inbox/drives/xyz/<fileId>.payload".
        const string sourceBucket = "source-bucket";
        const string sourceFullKey = "inbox/drives/xyz/abc123.payload";
        const string destRelPath = "drives/xyz/abc123.payload";

        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.CopyFromBucketAsync(
                sourceBucket, sourceFullKey, destRelPath, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var s3Source = new FakeStore(StorageBackendType.S3, (sourceBucket, sourceFullKey));

        await Sut(storage.Object).IngestFromAsync(s3Source, "drives/xyz/abc123.payload", destRelPath);

        // Verify the full source key (including source rootPath) was passed through, not just the relative path.
        storage.Verify(x => x.CopyFromBucketAsync(
            sourceBucket, sourceFullKey, destRelPath, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task IngestFrom_S3_Source_With_Null_Location_Throws_DriveFileStoreException()
    {
        // GetS3Location returns null -> must throw, not attempt a copy.
        var storage = new Mock<IS3Storage>(MockBehavior.Strict);
        var s3Source = new FakeStore(StorageBackendType.S3, s3Location: null);

        var caught = await Capture(() => Sut(storage.Object).IngestFromAsync(s3Source, "src/key", "dst/key"));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        // Storage must not be touched.
        storage.VerifyNoOtherCalls();
    }

    [Test]
    public async Task IngestFrom_Disk_Wraps_Failure_In_DriveFileStoreException()
    {
        var failure = S3Failure(HttpStatusCode.Forbidden);
        var storage = new Mock<IS3Storage>();
        storage.Setup(x => x.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(failure);

        var diskSource = new FakeStore(StorageBackendType.Disk);
        var caught = await Capture(() => Sut(storage.Object).IngestFromAsync(diskSource, "/tmp/f", "k"));

        Assert.That(caught, Is.InstanceOf<DriveFileStoreException>());
        Assert.That(caught!.InnerException, Is.SameAs(failure));
    }

    // --- GetS3Location ---

    [Test]
    public void GetS3Location_Returns_Bucket_And_FullKey_Combining_RootPath()
    {
        // S3FileStore.GetS3Location(rel) must return (bucket, rootPath + "/" + rel).
        var storage = new Mock<IS3Storage>();
        storage.SetupGet(x => x.BucketName).Returns("my-bucket");
        storage.Setup(x => x.GetFullKey("drives/d1/abc.payload")).Returns("inbox/drives/d1/abc.payload");

        var loc = Sut(storage.Object).GetS3Location("drives/d1/abc.payload");

        Assert.That(loc, Is.Not.Null);
        Assert.That(loc!.Value.bucket, Is.EqualTo("my-bucket"));
        Assert.That(loc!.Value.fullKey, Is.EqualTo("inbox/drives/d1/abc.payload"));
    }

}

#if RUN_S3_TESTS

public class S3FileStoreTests : PayloadReaderWriterBaseTestFixture
{
    private string _bucketName = "";
    private IAmazonS3 _s3Client = null!;
    private IS3Storage _s3Storage = null!;
    private MinioContainer _minioContainer = null!;

    [SetUp]
    public async Task Setup()
    {
        BaseSetup();
        TestSecrets.Load();

        _minioContainer = new MinioBuilder()
            .WithImage("minio/minio:RELEASE.2025-05-24T17-08-30Z")
            .WithUsername("minioadmin")
            .WithPassword("minioadmin123")
            .Build();
        await _minioContainer.StartAsync();

        _s3Client = new AmazonS3Client(
            _minioContainer.GetAccessKey(),
            _minioContainer.GetSecretKey(),
            new AmazonS3Config
            {
                ServiceURL = _minioContainer.GetConnectionString(),
                AuthenticationRegion = "foo",
                ForcePathStyle = true,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
            });

        _bucketName = $"zz-ci-test-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(_bucketName);

        var logger = TestLogFactory.CreateConsoleLogger<S3AwsStorage>();
        _s3Storage = new S3AwsStorage(logger, _s3Client, _bucketName, "drives");
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            await DeleteAllObjectsAsync(_bucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _bucketName });
        }
        finally
        {
            BaseTearDown();
            if (_minioContainer != null) await _minioContainer.DisposeAsync();
        }
    }

    private async Task DeleteAllObjectsAsync(string bucketName)
    {
        string? continuationToken = null;
        do
        {
            var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            { BucketName = bucketName, ContinuationToken = continuationToken, MaxKeys = 1000 });
            if (listResponse.S3Objects is { Count: > 0 })
            {
                await _s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = bucketName,
                    Objects = listResponse.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                });
            }
            continuationToken = listResponse.NextContinuationToken;
        }
        while (continuationToken != null);
    }

    private S3FileStore CreateSut() =>
        new(_s3Storage, TestLogFactory.CreateConsoleLogger<S3FileStore>(),
            new OdinConfiguration
            {
                S3Storage = new OdinConfiguration.S3StorageSection { RetryAttempts = 5, RetryInitialBackoffMs = 50 }
            });

    [Test]
    public async Task WriteStream_ThenReadAllBytes_RoundTrips()
    {
        var sut = CreateSut();
        var path = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}.metadata";
        var bytes = "hello s3filestore".ToUtf8ByteArray();

        using var stream = new MemoryStream(bytes);
        var written = await sut.WriteStreamAsync(path, stream);
        await Task.Delay(100);

        Assert.That(written, Is.EqualTo(bytes.Length));
        Assert.That(await sut.ExistsAsync(path), Is.True);
        Assert.That(await sut.ReadAllBytesAsync(path), Is.EqualTo(bytes));
    }

    [Test]
    public async Task WriteBytes_ThenReadAllBytes_RoundTrips()
    {
        var sut = CreateSut();
        var path = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}.payload";
        var bytes = "write bytes test".ToUtf8ByteArray();

        await sut.WriteBytesAsync(path, bytes);
        await Task.Delay(100);

        Assert.That(await sut.ReadAllBytesAsync(path), Is.EqualTo(bytes));
    }

    [Test]
    public async Task ReadBytes_ReturnsSlice()
    {
        var sut = CreateSut();
        var path = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}.bin";
        var bytes = new byte[] { 10, 20, 30, 40, 50 };

        await sut.WriteBytesAsync(path, bytes);
        await Task.Delay(100);

        var slice = await sut.ReadBytesAsync(path, 1, 3);
        Assert.That(slice, Is.EqualTo(new byte[] { 20, 30, 40 }));
    }

    [Test]
    public async Task LengthAsync_ReturnsCorrectLength()
    {
        var sut = CreateSut();
        var path = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}.bin";
        var bytes = new byte[] { 1, 2, 3, 4, 5 };

        await sut.WriteBytesAsync(path, bytes);
        await Task.Delay(100);

        Assert.That(await sut.LengthAsync(path), Is.EqualTo(5L));
    }

    [Test]
    public async Task DeleteAsync_RemovesFile()
    {
        var sut = CreateSut();
        var path = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}.bin";
        await sut.WriteBytesAsync(path, new byte[] { 1 });
        await Task.Delay(100);

        await sut.DeleteAsync(path);
        await Task.Delay(100);

        Assert.That(await sut.ExistsAsync(path), Is.False);
    }

    [Test]
    public async Task DeleteSetAsync_DeletesOnlyMatchingFileId()
    {
        var sut = CreateSut();
        var driveDir = $"{Guid.NewGuid():N}/drives/{Guid.NewGuid():N}";
        var fileId = Guid.NewGuid();
        var otherId = Guid.NewGuid();

        async Task Write(string key)
        {
            using var s = new MemoryStream("x".ToUtf8ByteArray());
            await sut.WriteStreamAsync(key, s);
        }

        var t1 = $"{driveDir}/{fileId:N}.metadata";
        var t2 = $"{driveDir}/{fileId:N}.foo-1.payload";
        var survivor = $"{driveDir}/{otherId:N}.foo-2.payload";
        await Write(t1); await Write(t2); await Write(survivor);
        await Task.Delay(100);

        await sut.DeleteSetAsync(driveDir, fileId);
        await Task.Delay(100);

        Assert.That(await sut.ExistsAsync(t1), Is.False);
        Assert.That(await sut.ExistsAsync(t2), Is.False);
        Assert.That(await sut.ExistsAsync(survivor), Is.True);
    }

    [Test]
    public async Task DeleteSetAsync_NoThrow_WhenNothingMatches()
    {
        var sut = CreateSut();
        var driveDir = $"{Guid.NewGuid():N}/drives/{Guid.NewGuid():N}";
        await sut.DeleteSetAsync(driveDir, Guid.NewGuid()); // must not throw
    }

    [Test]
    public async Task EnsureDirectoryAsync_IsNoOp()
    {
        var sut = CreateSut();
        await sut.EnsureDirectoryAsync("any/dir"); // must not throw
        Assert.Pass();
    }

    [Test]
    public async Task IngestFromAsync_Disk_To_S3_UploadsFile()
    {
        var sut = CreateSut();
        var content = "disk to s3 ingest".ToUtf8ByteArray();
        var srcFile = Path.Combine(TestRootPath, $"{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(srcFile, content);

        var destKey = $"{Guid.NewGuid():N}/ingested.bin";

        // Provide a disk-backed source store (Backend == Disk).
        var diskSource = new Mock<IDriveFileStore>();
        diskSource.SetupGet(x => x.Backend).Returns(StorageBackendType.Disk);
        diskSource.Setup(x => x.GetS3Location(It.IsAny<string>())).Returns((string _) => null);

        await sut.IngestFromAsync(diskSource.Object, srcFile, destKey);
        await Task.Delay(100);

        Assert.That(await sut.ExistsAsync(destKey), Is.True);
        Assert.That(await sut.ReadAllBytesAsync(destKey), Is.EqualTo(content));
    }

    [Test]
    public void ReadAllBytes_Missing_Throws_DriveFileStoreException()
    {
        var sut = CreateSut();
        var path = $"{Guid.NewGuid():N}/{Guid.NewGuid():N}.missing";
        Assert.ThrowsAsync<DriveFileStoreException>(async () => await sut.ReadAllBytesAsync(path));
    }

    // Two-bucket cross-bucket server-side copy regression test.
    // Source: bucket A, rootPath "inbox".  Dest: bucket B, rootPath "payloads".
    // Verifies that the full source key (inbox/...) is used, not just the relative path.
    [Test]
    public async Task IngestFromAsync_S3_To_S3_CrossBucket_CopiesObject()
    {
        // Create a second bucket for the destination.
        var destBucketName = $"zz-ci-dest-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(destBucketName);
        try
        {
            // Source store: bucket A, rootPath "inbox"
            var srcLogger = TestLogFactory.CreateConsoleLogger<S3AwsStorage>();
            IS3Storage srcS3 = new S3AwsStorage(srcLogger, _s3Client, _bucketName, "inbox");
            var srcStore = new S3FileStore(srcS3,
                TestLogFactory.CreateConsoleLogger<S3FileStore>(),
                new OdinConfiguration
                {
                    S3Storage = new OdinConfiguration.S3StorageSection { RetryAttempts = 5, RetryInitialBackoffMs = 50 }
                });

            // Destination store: bucket B, rootPath "payloads"
            var dstLogger = TestLogFactory.CreateConsoleLogger<S3AwsStorage>();
            IS3Storage dstS3 = new S3AwsStorage(dstLogger, _s3Client, destBucketName, "payloads");
            var dstStore = new S3FileStore(dstS3,
                TestLogFactory.CreateConsoleLogger<S3FileStore>(),
                new OdinConfiguration
                {
                    S3Storage = new OdinConfiguration.S3StorageSection { RetryAttempts = 5, RetryInitialBackoffMs = 50 }
                });

            // Write content via the SOURCE store at a relative key.
            // Source store applies rootPath "inbox" -> object lands at "inbox/drives/d1/<fileId>.payload" in bucket A.
            var fileId = Guid.NewGuid();
            var relKey = $"drives/d1/{fileId:N}.payload";
            var content = $"cross-bucket test {fileId:N}".ToUtf8ByteArray();
            await srcStore.WriteBytesAsync(relKey, content);
            await Task.Delay(100);

            // Confirm object exists in source at the full key.
            Assert.That(await srcStore.ExistsAsync(relKey), Is.True, "Source object should exist before copy");

            // Perform cross-bucket S3->S3 ingest.
            await dstStore.IngestFromAsync(srcStore, relKey, relKey);
            await Task.Delay(100);

            // Object must now exist in dest bucket at rootPath "payloads" + relKey.
            Assert.That(await dstStore.ExistsAsync(relKey), Is.True, "Destination object should exist after copy");
            Assert.That(await dstStore.ReadAllBytesAsync(relKey), Is.EqualTo(content), "Destination bytes must match source");

            // Source object must still exist (copy, not move).
            Assert.That(await srcStore.ExistsAsync(relKey), Is.True, "Source object must still exist after copy");
        }
        finally
        {
            await DeleteAllObjectsAsync(destBucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = destBucketName });
        }
    }
}

// Regression test for the S3-inbox promote bug:
// When the inbox is on S3, CopyPayloadToLongTermAsync must dispatch through IngestFromAsync
// (source-backend aware), not through a local-disk read. This test exercises the
// InboxFileStore -> LongTermPayloadStore (S3->S3 cross-bucket) promote path end-to-end
// at the store level.
[TestFixture]
public class PromoteViaIngestFromTests
{
    private string _inboxBucketName = "";
    private string _payloadBucketName = "";
    private IAmazonS3 _s3Client = null!;
    private MinioContainer _minioContainer = null!;

    [SetUp]
    public async Task Setup()
    {
        TestSecrets.Load();

        _minioContainer = new MinioBuilder()
            .WithImage("minio/minio:RELEASE.2025-05-24T17-08-30Z")
            .WithUsername("minioadmin")
            .WithPassword("minioadmin123")
            .Build();
        await _minioContainer.StartAsync();

        _s3Client = new AmazonS3Client(
            _minioContainer.GetAccessKey(),
            _minioContainer.GetSecretKey(),
            new AmazonS3Config
            {
                ServiceURL = _minioContainer.GetConnectionString(),
                AuthenticationRegion = "foo",
                ForcePathStyle = true,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
            });

        _inboxBucketName = $"zz-ci-inbox-{Guid.NewGuid():N}";
        _payloadBucketName = $"zz-ci-payloads-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(_inboxBucketName);
        await _s3Client.PutBucketAsync(_payloadBucketName);
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            await DeleteAllObjectsAsync(_inboxBucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _inboxBucketName });
            await DeleteAllObjectsAsync(_payloadBucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _payloadBucketName });
        }
        finally
        {
            if (_minioContainer != null) await _minioContainer.DisposeAsync();
        }
    }

    private async Task DeleteAllObjectsAsync(string bucketName)
    {
        string? continuationToken = null;
        do
        {
            var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            { BucketName = bucketName, ContinuationToken = continuationToken, MaxKeys = 1000 });
            if (listResponse.S3Objects is { Count: > 0 })
            {
                await _s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = bucketName,
                    Objects = listResponse.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                });
            }
            continuationToken = listResponse.NextContinuationToken;
        }
        while (continuationToken != null);
    }

    private static OdinConfiguration Config() => new()
    {
        S3Storage = new OdinConfiguration.S3StorageSection { RetryAttempts = 5, RetryInitialBackoffMs = 50 }
    };

    // Reproduces the S3-inbox promote failure: staging written to inbox S3, then promoted to
    // payload S3 via IngestFromAsync. With the old CopyPayloadFileAsync path this would read
    // from local disk (missing file) and fail. With IngestFromAsync it does a server-side copy.
    [Test]
    public async Task Promote_S3Inbox_To_S3Payload_CopiesBytes_AndLeavesSourceIntact()
    {
        var driveId = Guid.NewGuid();
        var fileId = Guid.NewGuid();
        var payloadKey = "testkey1";

        // Build InboxFileStore: bucket A, rootPath "inbox"
        IS3InboxStorage inboxS3 = new S3AwsInboxStorage(
            TestLogFactory.CreateConsoleLogger<S3AwsInboxStorage>(),
            _s3Client, _inboxBucketName, "inbox");
        var inboxStore = new InboxFileStore(
            new S3FileStore(inboxS3, TestLogFactory.CreateConsoleLogger<S3FileStore>(), Config()));

        // Build LongTermPayloadStore: bucket B, rootPath "payloads"
        IS3PayloadStorage payloadS3 = new S3AwsPayloadStorage(
            TestLogFactory.CreateConsoleLogger<S3AwsPayloadStorage>(),
            _s3Client, _payloadBucketName, "payloads");
        var payloadStore = new LongTermPayloadStore(
            new S3FileStore(payloadS3, TestLogFactory.CreateConsoleLogger<S3FileStore>(), Config()));

        // Stage: write payload bytes into the inbox store at a relative key shaped like the real pipeline.
        var relKey = $"drives/{driveId:N}/{fileId:N}.{payloadKey}.payload";
        var content = $"promote-test payload {fileId:N}".ToUtf8ByteArray();
        await inboxStore.WriteBytesAsync(relKey, content);
        await Task.Delay(100);

        Assert.That(await inboxStore.ExistsAsync(relKey), Is.True, "Staged payload must exist in inbox before promote");

        // Promote: this is the code path exercised by CopyPayloadToLongTermAsync after the fix.
        var destRelKey = $"drives/{driveId:N}/{fileId:N}.{payloadKey}.payload";
        await payloadStore.IngestFromAsync(inboxStore, relKey, destRelKey);
        await Task.Delay(100);

        // Promoted payload must exist in the payload bucket with identical bytes.
        Assert.That(await payloadStore.ExistsAsync(destRelKey), Is.True, "Promoted payload must exist in payload store");
        Assert.That(await payloadStore.ReadAllBytesAsync(destRelKey), Is.EqualTo(content), "Promoted bytes must match staged bytes");

        // Source must still exist in inbox (copy, not move).
        Assert.That(await inboxStore.ExistsAsync(relKey), Is.True, "Staged payload must still exist in inbox after promote (copy not move)");
    }
}

// Regression guard for the storage-refactor fix (Task 9):
// Exercises LongTermStorageManager.CopyPayloadToLongTermAsync end-to-end with a real S3 inbox
// and a real S3 payload store. If someone reverts the fix (replacing IngestFromAsync with the
// old disk-only CopyPayloadFileAsync), this test will fail because the inbox file lives in S3,
// not on disk.
[TestFixture]
public class LongTermStorageManagerPromoteTests
{
    private string _inboxBucketName = "";
    private string _payloadBucketName = "";
    private IAmazonS3 _s3Client = null!;
    private MinioContainer _minioContainer = null!;

    [SetUp]
    public async Task Setup()
    {
        TestSecrets.Load();

        _minioContainer = new MinioBuilder()
            .WithImage("minio/minio:RELEASE.2025-05-24T17-08-30Z")
            .WithUsername("minioadmin")
            .WithPassword("minioadmin123")
            .Build();
        await _minioContainer.StartAsync();

        _s3Client = new AmazonS3Client(
            _minioContainer.GetAccessKey(),
            _minioContainer.GetSecretKey(),
            new AmazonS3Config
            {
                ServiceURL = _minioContainer.GetConnectionString(),
                AuthenticationRegion = "foo",
                ForcePathStyle = true,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED
            });

        _inboxBucketName = $"zz-ci-inbox-{Guid.NewGuid():N}";
        _payloadBucketName = $"zz-ci-payloads-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(_inboxBucketName);
        await _s3Client.PutBucketAsync(_payloadBucketName);
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            await DeleteAllObjectsAsync(_inboxBucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _inboxBucketName });
            await DeleteAllObjectsAsync(_payloadBucketName);
            await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _payloadBucketName });
        }
        finally
        {
            if (_minioContainer != null) await _minioContainer.DisposeAsync();
        }
    }

    private async Task DeleteAllObjectsAsync(string bucketName)
    {
        string? continuationToken = null;
        do
        {
            var listResponse = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
            { BucketName = bucketName, ContinuationToken = continuationToken, MaxKeys = 1000 });
            if (listResponse.S3Objects is { Count: > 0 })
            {
                await _s3Client.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = bucketName,
                    Objects = listResponse.S3Objects.Select(o => new KeyVersion { Key = o.Key }).ToList()
                });
            }
            continuationToken = listResponse.NextContinuationToken;
        }
        while (continuationToken != null);
    }

    private static OdinConfiguration S3Config(Guid tenantId) => new()
    {
        Host = new OdinConfiguration.HostSection { TenantDataRootPath = "/tmp/odin-test" },
        S3Storage = new OdinConfiguration.S3StorageSection { RetryAttempts = 5, RetryInitialBackoffMs = 50 },
        S3Payload = new OdinConfiguration.S3PayloadSection { Enabled = true },
        S3Inbox = new OdinConfiguration.S3InboxSection { Enabled = true }
    };

    /// <summary>
    /// Drives CopyPayloadToLongTermAsync (the fixed method) end-to-end:
    ///   - inbox store is S3 (bucket A, rootPath "inbox")
    ///   - payload store is S3 (bucket B, rootPath "payloads")
    ///   - TenantPathManager is constructed in S3 mode so it emits relative keys
    ///   - The raw S3 client asserts the object landed at the FULL key in bucket B
    ///     (rootPath applied exactly once) and that the source still exists in bucket A.
    ///
    /// If the fix is reverted to CopyPayloadFileAsync the call will fail because the
    /// payload bytes are only in S3, not on local disk.
    /// </summary>
    [Test]
    public async Task CopyPayloadToLongTermAsync_S3Inbox_To_S3Payload_CopiesBytes_AndLeavesSourceIntact()
    {
        var tenantId = Guid.NewGuid();
        var driveId = Guid.NewGuid();
        var targetFileId = Guid.NewGuid();
        // payloadKey must match ^[a-z0-9_]{8,10}$
        var payloadKey = "testky01";
        var payloadUid = new UnixTimeUtcUnique(UnixTimeUtc.Now().milliseconds);

        // --- Config: both inbox and payloads on S3 ---
        var config = S3Config(tenantId);

        // --- TenantPathManager in S3 mode ---
        // With S3PayloadsEnabled=true: PayloadsPath = tenantId (no disk prefix), anchored to bucket root.
        // With S3InboxEnabled=true:    InboxPath    = tenantId (no disk prefix), anchored to bucket root.
        var pathManager = new TenantPathManager(config, tenantId);

        // --- TenantContext carrying the path manager ---
        var tenantContext = new TenantContext(
            dotYouRegistryId: Guid.NewGuid(),
            hostOdinId: new OdinId("test.example"),
            tenantPathManager: pathManager,
            firstRunToken: null,
            isPreconfigured: false,
            markedForDeletionDate: null,
            email: "test@example.com");

        // --- Inbox store: bucket A, rootPath "inbox" ---
        IS3InboxStorage inboxS3 = new S3AwsInboxStorage(
            TestLogFactory.CreateConsoleLogger<S3AwsInboxStorage>(),
            _s3Client, _inboxBucketName, "inbox");
        var inboxStore = new InboxFileStore(
            new S3FileStore(inboxS3, TestLogFactory.CreateConsoleLogger<S3FileStore>(), config));

        // --- Payload store: bucket B, rootPath "payloads" ---
        IS3PayloadStorage payloadS3 = new S3AwsPayloadStorage(
            TestLogFactory.CreateConsoleLogger<S3AwsPayloadStorage>(),
            _s3Client, _payloadBucketName, "payloads");
        var payloadFileStore = new S3FileStore(payloadS3, TestLogFactory.CreateConsoleLogger<S3FileStore>(), config);
        var longTermPayloadStore = new LongTermPayloadStore(payloadFileStore);

        // --- StorageDrive with a known Id ---
        var driveData = new StorageDriveData
        {
            Id = driveId,
            TempOriginalDriveId = driveId,
            Name = "test-drive",
            TargetDriveInfo = new TargetDrive { Alias = Guid.NewGuid(), Type = Guid.NewGuid() },
        };
        var drive = new StorageDrive(pathManager, driveData);

        // --- LongTermStorageManager: only longTermPayloadStore + tenantContext are used by CopyPayloadToLongTermAsync ---
        var manager = new LongTermStorageManager(
            logger: new Mock<ILogger<LongTermStorageManager>>().Object,
            longTermPayloadStore: longTermPayloadStore,
            driveQuery: null!,
            scopedIdentityTransactionFactory: null!,
            tableDriveTransferHistory: null!,
            driveMainIndex: null!,
            tenantContext: tenantContext,
            forgottenTasks: null!);

        // --- PayloadDescriptor ---
        var descriptor = new PayloadDescriptor
        {
            Key = payloadKey,
            Uid = payloadUid,
            ContentType = "application/octet-stream",
            Thumbnails = new System.Collections.Generic.List<ThumbnailDescriptor>()
        };

        // --- Stage the source payload in the inbox bucket ---
        // Production builds: sourceFilePath = Path.Combine(GetDriveInboxPath(driveId), GetFilename(fileId, payloadExtension))
        // GetDriveInboxPath(driveId) in S3-inbox mode = <tenant>/drives/<driveId:N>
        // GetFilename(fileId, payloadExtension) = <fileId:N>.<key>-<uid>.payload
        // (note: GetFilename uses "." as separator before the extension, then the extension already contains the payload details)
        var payloadExtension = TenantPathManager.GetBasePayloadFileNameAndExtension(payloadKey, payloadUid);
        var sourceFile = Path.Combine(
            pathManager.GetDriveInboxPath(driveId),
            TenantPathManager.GetFilename(targetFileId, payloadExtension));

        var content = $"manager-level-promote-test {targetFileId:N}".ToUtf8ByteArray();
        await inboxStore.WriteBytesAsync(sourceFile, content);
        await Task.Delay(100);

        Assert.That(await inboxStore.ExistsAsync(sourceFile), Is.True, "Source payload must exist in inbox before promote");

        // --- Exercise the actual fixed method ---
        await manager.CopyPayloadToLongTermAsync(drive, targetFileId, descriptor, sourceFile, inboxStore);
        await Task.Delay(100);

        // --- Compute expected destination relative key ---
        // GetPayloadDirectoryAndFileName returns: <tenant>/drives/<driveId:N>/files/<hi>/<lo>/<fileId:N>-<key>-<uid>.payload
        var destRelKey = pathManager.GetPayloadDirectoryAndFileName(driveId, targetFileId, payloadKey, payloadUid);

        // --- Assert via the store abstraction (basic) ---
        Assert.That(await longTermPayloadStore.ExistsAsync(destRelKey), Is.True,
            "Promoted payload must exist in payload store (via store abstraction)");
        Assert.That(await longTermPayloadStore.ReadAllBytesAsync(destRelKey), Is.EqualTo(content),
            "Promoted bytes must match staged bytes");

        // --- Assert via the RAW S3 client ---
        // The payload store has rootPath "payloads", so the full S3 key is "payloads/" + destRelKey.
        var expectedFullKey = "payloads/" + destRelKey;

        // 1. Object must exist in bucket B at the full key.
        using (var getResp = await _s3Client.GetObjectAsync(_payloadBucketName, expectedFullKey))
        using (var ms = new MemoryStream())
        {
            await getResp.ResponseStream.CopyToAsync(ms);
            Assert.That(ms.ToArray(), Is.EqualTo(content),
                $"Raw S3 get of '{expectedFullKey}' in payload bucket must return the staged bytes");
        }

        // 2. The object must NOT exist at the bare relative key without the rootPath prefix
        //    (proves rootPath was applied exactly once, not zero times).
        var notFoundEx = Assert.ThrowsAsync<Amazon.S3.AmazonS3Exception>(
            async () => await _s3Client.GetObjectAsync(_payloadBucketName, destRelKey));
        Assert.That(notFoundEx!.StatusCode, Is.EqualTo(System.Net.HttpStatusCode.NotFound),
            $"Key without 'payloads/' prefix must NOT exist in payload bucket (rootPath applied once)");

        // 3. Source must still exist in inbox bucket (copy, not move).
        var expectedInboxFullKey = "inbox/" + sourceFile;
        using var inboxResp = await _s3Client.GetObjectAsync(_inboxBucketName, expectedInboxFullKey);
        Assert.That(inboxResp.HttpStatusCode, Is.EqualTo(System.Net.HttpStatusCode.OK),
            "Source object must still exist in inbox after promote (copy not move)");
    }
}

#endif
