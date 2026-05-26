using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage.ObjectStorage;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Test.Helpers.Logging;
using Odin.Test.Helpers.Secrets;
using Testcontainers.Minio;

namespace Odin.Services.Tests.Drives.DriveCore.Storage;

#if RUN_S3_TESTS

public class InboxS3ReaderWriterTests
{
    private string _bucketName = "";
    private IAmazonS3 _s3Client = null!;
    private IS3InboxStorage _s3InboxStorage = null!;
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

        _bucketName = $"zz-ci-inbox-{Guid.NewGuid():N}";
        await _s3Client.PutBucketAsync(_bucketName);

        var logger = TestLogFactory.CreateConsoleLogger<S3AwsInboxStorage>();
        _s3InboxStorage = new S3AwsInboxStorage(logger, _s3Client, _bucketName, "inbox");
    }

    [TearDown]
    public async Task TearDown()
    {
        await DeleteAllObjectsAsync(_bucketName);
        await _s3Client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = _bucketName });
        await _minioContainer.DisposeAsync();
    }

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

    private InboxS3ReaderWriter CreateRw()
        => new(TestLogFactory.CreateConsoleLogger<InboxS3ReaderWriter>(), _s3InboxStorage);

    [Test]
    public async Task WriteStream_Read_Exists_RoundTrips()
    {
        var rw = CreateRw();
        var key = $"tenant/drives/{Guid.NewGuid():N}/{Guid.NewGuid():N}.metadata";
        var bytes = "hello".ToUtf8ByteArray();
        using var ms = new MemoryStream(bytes);

        var written = await rw.WriteStreamAsync(key, ms);
        Assert.That(written, Is.EqualTo((uint)bytes.Length));
        Assert.That(await rw.FileExistsAsync(key), Is.True);
        Assert.That(await rw.GetFileBytesAsync(key), Is.EqualTo(bytes));
    }

    [Test]
    public async Task DeleteByPrefix_RemovesAllPartsOfOneFileId()
    {
        var rw = CreateRw();
        var dir = $"tenant/drives/{Guid.NewGuid():N}";
        var fileId = Guid.NewGuid().ToString("N");
        var other = Guid.NewGuid().ToString("N");

        foreach (var ext in new[] { "metadata", "payload", "transferkeyheader" })
        {
            using var ms = new MemoryStream("x".ToUtf8ByteArray());
            await rw.WriteStreamAsync($"{dir}/{fileId}.{ext}", ms);
        }
        using (var keepMs = new MemoryStream("k".ToUtf8ByteArray()))
        {
            await rw.WriteStreamAsync($"{dir}/{other}.metadata", keepMs);
        }

        await rw.DeleteByPrefixAsync($"{dir}/{fileId}.");

        Assert.That(await rw.FileExistsAsync($"{dir}/{fileId}.metadata"), Is.False);
        Assert.That(await rw.FileExistsAsync($"{dir}/{fileId}.payload"), Is.False);
        Assert.That(await rw.FileExistsAsync($"{dir}/{other}.metadata"), Is.True);
    }

    [Test]
    public async Task PromoteToAsync_ServerSideCopiesInboxObjectToResolvedDest()
    {
        var rw = CreateRw();
        var inboxRel = $"tenant/drives/{Guid.NewGuid():N}/{Guid.NewGuid():N}.payload";
        var bytes = "promote-me".ToUtf8ByteArray();
        using (var ms = new MemoryStream(bytes))
        {
            await rw.WriteStreamAsync(inboxRel, ms);
        }

        // Resolved destination key in the same bucket (mimics payloadReaderWriter.ResolveObjectKey(...)).
        var destKey = $"payloads/tenant/drives/{Guid.NewGuid():N}/dest.payload";
        await rw.PromoteToAsync(inboxRel, destKey);

        // Read the dest directly via the S3 client to confirm the server-side copy landed at the absolute key.
        var resp = await _s3Client.GetObjectAsync(_bucketName, destKey);
        using var r = new StreamReader(resp.ResponseStream);
        var got = await r.ReadToEndAsync();
        Assert.That(got, Is.EqualTo("promote-me"));

        // Source still present (copy, not move).
        Assert.That(await rw.FileExistsAsync(inboxRel), Is.True);
    }
}

#endif
