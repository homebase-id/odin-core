using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.DriveWrite;

/// <summary>
/// Port of <c>_Universal/DriveTests/UpdateBatch/HammerTimeDirectDriveLocalUpdateBatchTests</c>. Two
/// threads drive update-batch (<see cref="UpdateLocale.Local"/>) against one shared file a hundred
/// times each, and every successful round is fully verified — metadata, payload list, payload fetch
/// and thumbnails. A losing writer must come back as a <c>400</c> carrying
/// <see cref="OdinClientErrorCode.VersionTagMismatch"/>, from which it re-reads the header and rejoins;
/// any other failure throws.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> update-batch endpoint through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>owner.V1.Drive</c>.
///
/// The concurrency shape is carried over (<see cref="PerformanceFramework.ThreadedTestAsync"/>,
/// 2 threads x 100 iterations); the original's random 5-50 ms inter-iteration sleep is dropped, as
/// nothing is pending across it. Each thread's work is ordinary HTTP against the test server, so every
/// request gets its own DI scope — the <c>ScopedConnectionFactory</c> single-scope hazard does not arise.
///
/// Both threads mutate the single shared <c>_originalFileMetadata</c> / <c>_originalPayload</c>
/// instances, exactly as the original did. That is a race, and a benign-looking one: the two threads
/// write the same constant content and data type, so the values they read back are the values they
/// assert on either way. Carried as-is rather than fixed inside a port.
/// </remarks>
[TestFixture]
public class HammerTimeLocalUpdateBatchTests : V2Fixture
{
    private OwnerSession _owner;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;

    private UploadFileMetadata _originalFileMetadata;
    private TestPayloadDefinition _originalPayload;
    private int _successCount;
    private int _conflictCount;

    [Test]
    public async Task UpdateBatch_HammerTime_WithPayloads()
    {
        _owner = await LoginAsOwner();
        var targetDrive = TargetDrive.NewTargetDrive();
        await _owner.Admin.CreateDrive(targetDrive, "Test Drive 001", allowAnonymousReads: true);

        //
        // Setup - upload a new file with payloads
        //
        _originalFileMetadata = SampleMetadataData.Create(fileType: 100);
        _originalPayload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = [_originalPayload.ToPayloadDescriptor()]
        };

        var uploadNewFileResponse = await _owner.V1.Drive.UploadNewFile(targetDrive,
            _originalFileMetadata, uploadManifest, [_originalPayload]);

        Assert.That(uploadNewFileResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var uploadResult = uploadNewFileResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);
        _targetFile = uploadResult!.File;
        _initialVersionTag = uploadResult.NewVersionTag;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 2, iterations: 100, OverwriteUsingUpdateBatch);

        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Conflict Count: {_conflictCount}");
    }

    private async Task<(long, long[])> OverwriteUsingUpdateBatch(int threadNumber, int iterations)
    {
        long[] timers = new long[iterations];
        var sw = new Stopwatch();

        Guid newVersionTag = _initialVersionTag;

        for (int count = 0; count < iterations; count++)
        {
            sw.Restart();

            // change around some data
            _originalFileMetadata.AppData.Content = "some new content here";
            _originalFileMetadata.AppData.DataType = 777;

            var randomPayloadContent = string.Join("", Enumerable.Range(2468, 2468).Select(_ => Guid.NewGuid().ToString("N")));

            _originalPayload.Content = randomPayloadContent.ToUtf8ByteArray();
            var testPayloads = new List<TestPayloadDefinition>()
            {
                _originalPayload
            };

            var prevTag = newVersionTag;
            var (status, oce, versionTag) = await UploadAndValidatePayload(newVersionTag, testPayloads);

            if (status == HttpStatusCode.OK)
            {
                Assert.That(versionTag.HasValue, Is.True);
                newVersionTag = versionTag.GetValueOrDefault();
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
        }

        return (0, timers);
    }

    private async Task<(HttpStatusCode status, OdinClientErrorCode? oce, Guid? versionTag)> UploadAndValidatePayload(
        Guid targetVersionTag,
        List<TestPayloadDefinition> testPayloads)
    {
        _originalFileMetadata.VersionTag = targetVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = _targetFile.ToFileIdentifier(),
            Recipients = default,
            Manifest = new UploadManifest
            {
                // in this test we are just overwriting payloads
                PayloadDescriptors = testPayloads.Select(tpd => tpd.ToPayloadDescriptor(
                    PayloadUpdateOperationType.AppendOrOverwrite)).ToList()
            }
        };

        var updateFileResponse = await _owner.V1.Drive.UpdateFile(updateInstructionSet, _originalFileMetadata, [_originalPayload]);

        // bounce if it's a bad request
        if (updateFileResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            var oce = TestUtils.ParseProblemDetails(updateFileResponse.Error);
            return (updateFileResponse.StatusCode, oce, null);
        }

        _successCount++;

        //
        // Get the updated file and test it
        //
        var getHeaderResponse = await _owner.V1.Drive.GetFileHeader(_targetFile);
        Assert.That(getHeaderResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var header = getHeaderResponse.Content;
        Assert.That(header, Is.Not.Null);
        Assert.That(header.FileMetadata.AppData.Content, Is.EqualTo(_originalFileMetadata.AppData.Content));
        Assert.That(header.FileMetadata.AppData.DataType, Is.EqualTo(_originalFileMetadata.AppData.DataType));
        Assert.That(header.FileMetadata.Payloads.Count(), Is.EqualTo(1));
        Assert.That(header.FileMetadata.Payloads.Select(pd => pd.Key), Does.Contain(_originalPayload.Key), "missing payload");

        //
        // Ensure payloadToAdd add is added
        //
        var getPayloadToAddResponse = await _owner.V1.Drive.GetPayload(_targetFile, _originalPayload.Key);
        Assert.That(getPayloadToAddResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue, Is.True);
        Assert.That(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault(),
            Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

        // var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
        // CollectionAssert.AreEqual(content, _originalPayload.Content);

        // Check all the thumbnails
        foreach (var thumbnail in _originalPayload.Thumbnails)
        {
            var getThumbnailResponse = await _owner.V1.Drive.GetThumbnail(_targetFile, thumbnail.PixelWidth,
                thumbnail.PixelHeight, _originalPayload.Key);

            Assert.That(getThumbnailResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(getThumbnailResponse.ContentHeaders!.LastModified.HasValue, Is.True);
            Assert.That(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault(),
                Is.LessThan(DateTimeOffset.Now.AddSeconds(10)));

            var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
            Assert.That(thumbContent, Is.EqualTo(thumbnail.Content));
        }

        return (updateFileResponse.StatusCode, OdinClientErrorCode.NoErrorCode, updateFileResponse.Content!.NewVersionTag);
    }
}
