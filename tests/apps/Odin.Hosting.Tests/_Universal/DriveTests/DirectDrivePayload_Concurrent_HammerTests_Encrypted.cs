using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayload_Concurrent_HammerTests_Encrypted
{
    private WebScaffold _scaffold;

    private OwnerApiClientRedux _ownerApiClient;
    private TargetDrive _targetDrive;

    //private Guid _initialVersionTag;
    //private ExternalFileIdentifier _targetFile;
    // private KeyHeader _metadataKeyHeader;

    private int _successCount;
    private int _badRequestCount;
    
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


    [Test]
    public async Task Overwrite_Encrypted_PayloadManyTimes_Concurrently_MultipleThreads()
    {
        var identity = TestIdentities.Pippin;
        _targetDrive = TargetDrive.NewTargetDrive();
        _ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await PerformanceFramework.ThreadedTestAsync(maxThreads: 9, iterations: 100, OverwritePayload);
        
        Console.WriteLine($"Success Count: {_successCount}");
        Console.WriteLine($"Bad Request Count: {_badRequestCount}");
    }

    private async Task<(long, long[])> OverwritePayload(int threadNumber, int iterations)
    {
        long[] timers = new long[iterations];
        var sw = new Stopwatch();
        int fileByteLength = 0;

        var identity = TestIdentities.Pippin;

        //
        // Prepare
        //
        var (uploadResult, metadataKeyHeader) = await PrepareEncryptedFile(identity, _targetDrive);
        var _targetFile = uploadResult.File;
        var _initialVersionTag = uploadResult.NewVersionTag;
        var _metadataKeyHeader = metadataKeyHeader;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await _ownerApiClient.DriveRedux.GetFileHeader(_targetFile);
        ClassicAssert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        ClassicAssert.IsNotNull(headerBeforeUpload);


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
            var tag  = await UploadAndValidatePayload(_targetFile, newVersionTag, uploadManifest, testPayloads, metadataKeyHeader);

            if (tag.HasValue)
            {
                newVersionTag = tag.GetValueOrDefault();
                ClassicAssert.IsTrue(prevTag != newVersionTag, "version tag did not change");
            }

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;

            // If you want to introduce a delay be sure to use: await Task.Delay(1);
            // await Task.Delay(0);
            // Take.Delay() is very inaccurate.
        }

        return (fileByteLength, timers);
    }

    private async Task<Guid?> UploadAndValidatePayload(ExternalFileIdentifier targetFile, Guid targetVersionTag, UploadManifest uploadManifest,
        List<TestPayloadDefinition> testPayloads, KeyHeader metadataKeyHeader)
    {
        var (uploadPayloadResponse, encryptedPayloads64) =
            await _ownerApiClient.DriveRedux.UploadEncryptedPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads, metadataKeyHeader.AesKey.GetKey());

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.OK)
        {
            _successCount++;
            // if it 
            ClassicAssert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");
            return uploadPayloadResponse.Content!.NewVersionTag;
        }

        if (uploadPayloadResponse.StatusCode == HttpStatusCode.BadRequest)
        {
            _badRequestCount++;
            //what to expect in this case?
        }

        return null;
    }

    private async Task<(UploadResult, KeyHeader keyHeader)> PrepareEncryptedFile(TestIdentity identity, TargetDrive targetDrive)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "just some test data";
        var keyHeader = KeyHeader.NewRandom16();

        var (uploadNewMetadataResponse, encryptedJsonContent64) = await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(
            targetDrive,
            uploadedFileMetadata,
            keyHeader: keyHeader);

        ClassicAssert.IsTrue(uploadNewMetadataResponse.IsSuccessStatusCode);
        var uploadResult = uploadNewMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        return (uploadResult, keyHeader);
    }
}