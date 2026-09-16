using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDrivePayload_Concurrent_HammerTests_Unencrypted</c>. Two
/// threads hammer the payload of one shared file a hundred times each. Its own summary: "It's less
/// about precise pass or fail but rather testing what range of things can occur given concurrency" —
/// every response must be either a success whose version tag moved, or a <c>400</c> carrying
/// <see cref="OdinClientErrorCode.VersionTagMismatch"/>, from which the loser re-reads the header and
/// rejoins the race. Any other outcome throws.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> payload endpoints through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>owner.V1.Drive</c>.
///
/// The concurrency shape is carried over unchanged (<see cref="PerformanceFramework.ThreadedTestAsync"/>,
/// 2 threads x 100 iterations, with the original's random 5-50 ms inter-iteration delay). Each thread's
/// work is ordinary HTTP against the test server, so every request gets its own DI scope — the
/// <c>ScopedConnectionFactory</c> single-scope hazard does not arise.
///
/// The counters stay plain <c>++</c> as in the original: they are printed, never asserted, so the race
/// they carry is pre-existing and inert.
/// </remarks>
[TestFixture]
public class PayloadConcurrentHammerUnencryptedTests : V2Fixture
{
    private OwnerSession _owner;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;

    private int _successCount;
    private int _conflictCount;

    /// <summary>
    /// This test will throw multiple threads at uploading a payload to a single-existing file.
    /// Since it's hard to predict the natural flow of so many threads, the test will perform
    /// various assertions based on the response code
    ///
    /// It's less about precise pass or fail but rather testing what range of things
    /// can occur given concurrency.
    /// </summary>
    [Test]
    public async Task OverwritePayloadManyTimes_Concurrently_MultipleThreads()
    {
        var targetDrive = TargetDrive.NewTargetDrive();
        _owner = await LoginAsOwner();

        //
        // Prepare
        //
        var uploadResult = await PrepareFile(targetDrive);
        _targetFile = uploadResult.File;
        _initialVersionTag = uploadResult.NewVersionTag;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await _owner.V1.Drive.GetFileHeader(_targetFile);
        Assert.That(getHeaderBeforeUploadResponse.IsSuccessStatusCode, Is.True);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        Assert.That(headerBeforeUpload, Is.Not.Null);

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 2, iterations: 100, OverwritePayload);

        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Conflict Count: {_conflictCount}");
    }

    private async Task<(long, long[])> OverwritePayload(int threadNumber, int iterations)
    {
        long[] timers = new long[iterations];
        var sw = new Stopwatch();
        int fileByteLength = 0;
        Random random = new Random();

        Guid newVersionTag = _initialVersionTag;
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
            var (status, oce, tag) = await UploadAndValidatePayload(_targetFile, newVersionTag, uploadManifest, testPayloads);

            if (status == HttpStatusCode.OK)
            {
                Assert.That(tag.HasValue, Is.True);
                newVersionTag = tag.GetValueOrDefault();
                Assert.That(newVersionTag, Is.Not.EqualTo(prevTag), $"version tag did not change on iteration {count}");
            }
            else
            {
                _conflictCount++;
                if (oce == OdinClientErrorCode.VersionTagMismatch)
                {
                    // we must presume there was a version tag mismatch, let's see if we can get back in the race
                    var getHeader = await _owner.V1.Drive.GetFileHeader(_targetFile);
                    newVersionTag = getHeader.Content.FileMetadata.VersionTag;
                }
                else
                {
                    throw new Exception($"Error uploading payload: HttpStatus {status}, OdinClientErrorCode {oce}");
                }
            }

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;

            await Task.Delay(random.Next(5, 51));
        }

        return (fileByteLength, timers);
    }

    private async Task<(HttpStatusCode, OdinClientErrorCode, Guid?)> UploadAndValidatePayload(ExternalFileIdentifier targetFile,
        Guid targetVersionTag,
        UploadManifest uploadManifest,
        List<TestPayloadDefinition> testPayloads)
    {
        var uploadPayloadResponse = await _owner.V1.Drive.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.OK)
        {
            _successCount++;
            Assert.That(uploadPayloadResponse.Content!.NewVersionTag, Is.Not.EqualTo(targetVersionTag),
                "Version tag should have changed");
            return (HttpStatusCode.OK, OdinClientErrorCode.NoErrorCode, uploadPayloadResponse.Content!.NewVersionTag);
        }

        Assert.That(uploadPayloadResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var oce = TestUtils.ParseProblemDetails(uploadPayloadResponse.Error);

        return (HttpStatusCode.BadRequest, oce, null);
    }

    private async Task<UploadResult> PrepareFile(TargetDrive targetDrive)
    {
        await _owner.Admin.CreateDrive(targetDrive, "Test Drive 001", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadNewMetadataResponse = await _owner.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        Assert.That(uploadNewMetadataResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadNewMetadataResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);

        return uploadResult;
    }
}
