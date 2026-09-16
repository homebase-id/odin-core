using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDrivePayload_Sequential_HammerTests_Unencrypted</c>. One
/// thread overwrites an unencrypted payload ten times in a row, verifying each round end to end: the
/// version tag moved, the header agrees with it, the descriptor matches what was sent, and the bytes
/// fetched back are the bytes that went in.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> payload endpoints through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>owner.V1.Drive</c>. Single-threaded by
/// construction (<see cref="PerformanceFramework.ThreadedTestAsync"/> with <c>maxThreads: 1</c>) and
/// kept that way, 100 ms per-iteration delay included.
/// </remarks>
[TestFixture]
public class PayloadSequentialHammerUnencryptedTests : V2Fixture
{
    private OwnerSession _owner;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;

    [Test]
    public async Task CanOverwritePayloadManyTimes_Sequentially_OneThread()
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

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 1, iterations: 10, OverwritePayload);
    }

    private async Task<(long, long[])> OverwritePayload(int threadNumber, int iterations)
    {
        long[] timers = new long[iterations];
        var sw = new Stopwatch();
        int fileByteLength = 0;

        var newVersionTag = _initialVersionTag;
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
            newVersionTag = await UploadAndValidatePayload(_targetFile, newVersionTag, uploadManifest, testPayloads);
            Assert.That(newVersionTag, Is.Not.EqualTo(prevTag), $"version tag did not change on iteration {count}");

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;

            await Task.Delay(100);
        }

        return (fileByteLength, timers);
    }

    private async Task<Guid> UploadAndValidatePayload(ExternalFileIdentifier targetFile, Guid targetVersionTag,
        UploadManifest uploadManifest, List<TestPayloadDefinition> testPayloads)
    {
        var uploadPayloadResponse = await _owner.V1.Drive.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);
        Assert.That(uploadPayloadResponse.IsSuccessStatusCode, Is.True);

        Assert.That(uploadPayloadResponse.Content!.NewVersionTag, Is.Not.EqualTo(targetVersionTag),
            "Version tag should have changed");

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await _owner.V1.Drive.GetFileHeader(targetFile);
        Assert.That(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode, Is.True);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        Assert.That(headerAfterPayloadWasUploaded, Is.Not.Null);

        Assert.That(headerAfterPayloadWasUploaded.FileMetadata.VersionTag, Is.EqualTo(uploadPayloadResponse.Content.NewVersionTag),
            "Version tag should match the one set by uploading the new payload");

        var uploadedPayloadDefinition = testPayloads.Single();

        // Payload should be listed
        Assert.That(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        var thePayloadDescriptor = headerAfterPayloadWasUploaded.FileMetadata.Payloads
            .SingleOrDefault(p => p.KeyEquals(uploadedPayloadDefinition.Key));
        Assert.That(thePayloadDescriptor, Is.Not.Null);
        Assert.That(thePayloadDescriptor.ContentType, Is.EqualTo(uploadedPayloadDefinition.ContentType));
        Assert.That(thePayloadDescriptor.Thumbnails, Is.EquivalentTo(uploadedPayloadDefinition.Thumbnails));
        Assert.That(thePayloadDescriptor.BytesWritten, Is.EqualTo(uploadedPayloadDefinition.Content.Length));

        // Last modified should be changed
        // ClassicAssert.IsTrue(thePayloadDescriptor.LastModified > headerBeforeUpload.FileMetadata.Updated);

        // Get the payload
        var getPayloadResponse = await _owner.V1.Drive.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        Assert.That(getPayloadResponse.IsSuccessStatusCode, Is.True);
        var payloadContent = await getPayloadResponse.Content.ReadAsStringAsync();
        Assert.That(payloadContent, Is.EqualTo(uploadedPayloadDefinition.Content.ToStringFromUtf8Bytes()));

        return uploadPayloadResponse.Content!.NewVersionTag;
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
