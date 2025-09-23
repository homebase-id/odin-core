using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayload_Sequential_HammerTests_Unencrypted
{
    private WebScaffold _scaffold;

    private OwnerApiClientRedux _ownerApiClient;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin });
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
    public async Task CanOverwritePayloadManyTimes_Sequentially_OneThread()
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
            ClassicAssert.IsTrue(prevTag != newVersionTag, "version tag did not change");

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;

            await Task.Delay(100);
        }

        return (fileByteLength, timers);
    }

    private async Task<Guid> UploadAndValidatePayload(ExternalFileIdentifier targetFile, Guid targetVersionTag, UploadManifest uploadManifest,
        List<TestPayloadDefinition> testPayloads)
    {
        var uploadPayloadResponse = await _ownerApiClient.DriveRedux.UploadPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads);
        ClassicAssert.IsTrue(uploadPayloadResponse.IsSuccessStatusCode);

        ClassicAssert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await _ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        ClassicAssert.IsNotNull(headerAfterPayloadWasUploaded);

        ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.VersionTag == uploadPayloadResponse.Content.NewVersionTag,
            "Version tag should match the one set by uploading the new payload");

        var uploadedPayloadDefinition = testPayloads.Single();

        // Payload should be listed 
        ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Count() == 1);
        var thePayloadDescriptor = headerAfterPayloadWasUploaded.FileMetadata.Payloads
            .SingleOrDefault(p => p.KeyEquals( uploadedPayloadDefinition.Key));
        ClassicAssert.IsNotNull(thePayloadDescriptor);
        ClassicAssert.IsTrue(thePayloadDescriptor.ContentType == uploadedPayloadDefinition.ContentType);
        CollectionAssert.AreEquivalent(thePayloadDescriptor.Thumbnails, uploadedPayloadDefinition.Thumbnails);
        ClassicAssert.IsTrue(thePayloadDescriptor.BytesWritten == uploadedPayloadDefinition.Content.Length);

        // Last modified should be changed
        // ClassicAssert.IsTrue(thePayloadDescriptor.LastModified > headerBeforeUpload.FileMetadata.Updated);

        // Get the payload
        var getPayloadResponse = await _ownerApiClient.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
        var payloadContent = await getPayloadResponse.Content.ReadAsStringAsync();
        ClassicAssert.IsTrue(payloadContent == uploadedPayloadDefinition.Content.ToStringFromUtf8Bytes());

        return uploadPayloadResponse.Content!.NewVersionTag;
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