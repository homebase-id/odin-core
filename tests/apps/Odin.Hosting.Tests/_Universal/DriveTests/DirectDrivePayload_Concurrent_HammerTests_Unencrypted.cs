using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;
using Odin.Core.Exceptions;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayload_Concurrent_HammerTests_Unencrypted
{
    private WebScaffold _scaffold;

    private OwnerApiClientRedux _ownerApiClient;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;

    private int _successCount;
    private int _ConflictCount;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }


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
        var identity = TestIdentities.Pippin;
        var targetDrive = TargetDrive.NewTargetDrive();
        _ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        //
        // Prepare
        //
        var uploadResult = await PrepareFile(identity, targetDrive);
        _targetFile = uploadResult.File;
        _initialVersionTag = uploadResult.NewVersionTag;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await _ownerApiClient.DriveRedux.GetFileHeader(_targetFile);
        ClassicAssert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        ClassicAssert.IsNotNull(headerBeforeUpload);

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 2, iterations: 100, OverwritePayload);

        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Conflict Count: {_ConflictCount}");
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
                ClassicAssert.IsTrue(tag.HasValue);
                newVersionTag = tag.GetValueOrDefault();
                ClassicAssert.IsTrue(prevTag != newVersionTag, "version tag did not change");
            }
            else
            {
                _ConflictCount++;
                if (oce == OdinClientErrorCode.VersionTagMismatch)
                {
                    // we must presume there was a version tag mismatch, let's see if we can get back in the race
                    var getHeader = await _ownerApiClient.DriveRedux.GetFileHeader(_targetFile);
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
        var uploadPayloadResponse = await _ownerApiClient.DriveRedux.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.OK)
        {
            _successCount++;
            // if it 
            ClassicAssert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");
            return (HttpStatusCode.OK, OdinClientErrorCode.NoErrorCode, uploadPayloadResponse.Content!.NewVersionTag);
        }
        ClassicAssert.IsTrue(uploadPayloadResponse.StatusCode == HttpStatusCode.BadRequest);

        var oce = TestUtils.ParseProblemDetails(uploadPayloadResponse.Error);
        
        return (HttpStatusCode.BadRequest, oce, null);
    }

    private async Task<UploadResult> PrepareFile(TestIdentity identity, TargetDrive targetDrive)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);

        var uploadNewMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        ClassicAssert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        return uploadResult;
    }
}