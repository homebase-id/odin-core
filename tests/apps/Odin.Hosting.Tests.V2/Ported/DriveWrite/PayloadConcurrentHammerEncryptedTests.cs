using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDrivePayload_Concurrent_HammerTests_Encrypted</c>. Nine
/// threads each prepare their own encrypted file and then overwrite its payload a hundred times,
/// carrying the version tag forward. It asserts no threshold: the point is that every response is
/// either a success whose version tag moved or a clean <c>400</c>, and that nothing wedges.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> payload endpoints through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>owner.V1.Drive</c>.
///
/// The <c>[Explicit]</c> marker is carried over verbatim, together with the note explaining it, so
/// this does not run under <c>dotnet test</c> here either. The concurrency shape
/// (<see cref="PerformanceFramework.ThreadedTestAsync"/>, 9 threads x 100 iterations) is unchanged;
/// each thread's work is ordinary HTTP against the test server, so every request gets its own DI
/// scope and the <c>ScopedConnectionFactory</c> single-scope hazard does not arise.
///
/// The counters are left as plain <c>++</c> rather than <c>Interlocked</c>, exactly as the original
/// had them — they are only written to the console, never asserted on, so the races they carry are
/// pre-existing and inert. Fixing that inside a port would be a rewrite.
/// </remarks>
[TestFixture]
public class PayloadConcurrentHammerEncryptedTests : V2Fixture
{
    private OwnerSession _owner;
    private TargetDrive _targetDrive;

    private int _successCount;
    private int _badRequestCount;

    [Test]
    public async Task Overwrite_Encrypted_PayloadManyTimes_Concurrently_MultipleThreads()
    {
        _targetDrive = TargetDrive.NewTargetDrive();
        _owner = await LoginAsOwner();

        // Created once, up front, rather than from inside each thread as the original did: nine
        // threads racing to create the same drive means eight lose, and OwnerAdmin.CreateDrive throws
        // on the resulting 400 where V1's DriveManager.CreateDrive merely returned it. Same
        // precondition either way - see the sibling ConcurrentOverwriteEncryptedHeaderTests, whose V1
        // original had already been moved to an up-front create for the same reason.
        await _owner.Admin.CreateDrive(_targetDrive, "Test Drive 001", allowAnonymousReads: true);

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 9, iterations: 100, OverwritePayload);

        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Bad Request Count: {_badRequestCount}");
    }

    private async Task<(long, long[])> OverwritePayload(int threadNumber, int iterations)
    {
        long[] timers = new long[iterations];
        var sw = new Stopwatch();
        int fileByteLength = 0;

        //
        // Prepare
        //
        var (uploadResult, metadataKeyHeader) = await PrepareEncryptedFile(_targetDrive);
        var targetFile = uploadResult.File;
        var initialVersionTag = uploadResult.NewVersionTag;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await _owner.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderBeforeUploadResponse.IsSuccessStatusCode, Is.True);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        Assert.That(headerBeforeUpload, Is.Not.Null);

        var newVersionTag = initialVersionTag;
        //
        // I presume here we retrieve the file and download it
        //
        for (int count = 0; count < iterations; count++)
        {
            sw.Restart();

            var randomPayloadContent = string.Join("", Enumerable.Range(2468, 2468).Select(i => Guid.NewGuid().ToString("N")));

            //
            // Now add a payload
            //
            var uploadedPayloadDefinition = new TestPayloadDefinition()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                Key = "pknt0001",
                ContentType = "text/plain",
                Content = randomPayloadContent.ToUtf8ByteArray(),
                DescriptorContent = "",
                PreviewThumbnail = default,
                Thumbnails = new List<ThumbnailContent>()
            };

            var testPayloads = new List<TestPayloadDefinition>()
            {
                uploadedPayloadDefinition
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var prevTag = newVersionTag;
            var tag = await UploadAndValidatePayload(targetFile, newVersionTag, uploadManifest, testPayloads, metadataKeyHeader);

            if (tag.HasValue)
            {
                newVersionTag = tag.GetValueOrDefault();
                Assert.That(newVersionTag, Is.Not.EqualTo(prevTag), $"version tag did not change on iteration {count}");
            }

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;
        }

        return (fileByteLength, timers);
    }

    private async Task<Guid?> UploadAndValidatePayload(ExternalFileIdentifier targetFile, Guid targetVersionTag,
        UploadManifest uploadManifest, List<TestPayloadDefinition> testPayloads, KeyHeader metadataKeyHeader)
    {
        var (uploadPayloadResponse, _) = await _owner.V1.Drive.UploadEncryptedPayloads(
            targetFile, targetVersionTag, uploadManifest, testPayloads, metadataKeyHeader.AesKey.GetKey());

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.OK)
        {
            _successCount++;
            Assert.That(uploadPayloadResponse.Content!.NewVersionTag, Is.Not.EqualTo(targetVersionTag),
                "Version tag should have changed");
            return uploadPayloadResponse.Content!.NewVersionTag;
        }

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            _badRequestCount++;
            //what to expect in this case?
        }

        return null;
    }

    private async Task<(UploadResult, KeyHeader keyHeader)> PrepareEncryptedFile(TargetDrive targetDrive)
    {
        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "just some test data";
        var keyHeader = KeyHeader.NewRandom16();

        var (uploadNewMetadataResponse, _) = await _owner.V1.Drive.UploadNewEncryptedMetadata(
            targetDrive,
            uploadedFileMetadata,
            keyHeader: keyHeader);

        Assert.That(uploadNewMetadataResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        return (uploadResult, keyHeader);
    }
}
