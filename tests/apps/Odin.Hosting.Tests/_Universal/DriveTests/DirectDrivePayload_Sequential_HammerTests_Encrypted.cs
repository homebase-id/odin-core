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
using Odin.Services.Peer.Encryption;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.Performance;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDrivePayload_Sequential_HammerTests_Encrypted
{
    private WebScaffold _scaffold;

    private OwnerApiClientRedux _ownerApiClient;
    private Guid _initialVersionTag;
    private ExternalFileIdentifier _targetFile;
    private KeyHeader _metadataKeyHeader;

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
        const int MAXTHREADS = 1;
        const int MAXITERATIONS = 50;

        var identity = TestIdentities.Pippin;
        var targetDrive = TargetDrive.NewTargetDrive();
        _ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        //
        // Prepare
        //
        var (uploadResult, metadataKeyHeader) = await PrepareEncryptedFile(identity, targetDrive);
        _targetFile = uploadResult.File;
        _initialVersionTag = uploadResult.NewVersionTag;
        _metadataKeyHeader = metadataKeyHeader;

        //
        // Get the header before we make changes so we have a baseline
        //
        var getHeaderBeforeUploadResponse = await _ownerApiClient.DriveRedux.GetFileHeader(_targetFile);
        ClassicAssert.IsTrue(getHeaderBeforeUploadResponse.IsSuccessStatusCode);
        var headerBeforeUpload = getHeaderBeforeUploadResponse.Content;
        ClassicAssert.IsNotNull(headerBeforeUpload);

        await PerformanceFramework.ThreadedTestAsync(MAXTHREADS, MAXITERATIONS, OverwritePayload);
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
            newVersionTag = await UploadAndValidatePayload(_targetFile, newVersionTag, uploadManifest, testPayloads);
            ClassicAssert.IsTrue(prevTag != newVersionTag, "version tag did not change");

            // Finished doing all the work
            timers[count] = sw.ElapsedMilliseconds;

            // If you want to introduce a delay be sure to use: await Task.Delay(1);
            await Task.Delay(100);
            // Take.Delay() is very inaccurate.
        }

        return (fileByteLength, timers);
    }

    private async Task<Guid> UploadAndValidatePayload(ExternalFileIdentifier targetFile, Guid targetVersionTag, UploadManifest uploadManifest,
        List<TestPayloadDefinition> testPayloads)
    {
        var (uploadPayloadResponse, encryptedPayloads64) =
            await _ownerApiClient.DriveRedux.UploadEncryptedPayloads(targetFile, targetVersionTag, uploadManifest, testPayloads, _metadataKeyHeader.AesKey.GetKey());
        ClassicAssert.IsTrue(uploadPayloadResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(uploadPayloadResponse.Content!.NewVersionTag != targetVersionTag, "Version tag should have changed");

        // Get the latest file header
        var getHeaderAfterPayloadUploadedResponse = await _ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderAfterPayloadUploadedResponse.IsSuccessStatusCode);
        var headerAfterPayloadWasUploaded = getHeaderAfterPayloadUploadedResponse.Content;
        ClassicAssert.IsNotNull(headerAfterPayloadWasUploaded);

        ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.VersionTag == uploadPayloadResponse.Content.NewVersionTag,
            "Version tag should match the one set by uploading the new payload");

        ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.IsEncrypted);

        var ownerSharedSecret = _ownerApiClient.GetTokenContext().SharedSecret;
        var mainKeyHeader = headerAfterPayloadWasUploaded.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ownerSharedSecret);

        var uploadedPayloadDefinition = testPayloads.Single();
        var encryptedPayloadBytes = encryptedPayloads64[uploadedPayloadDefinition.Key];
        var payloadKeyHeader = new KeyHeader()
        {
            Iv = uploadedPayloadDefinition.Iv,
            AesKey = mainKeyHeader.AesKey
        };

        // Payload should be listed 
        ClassicAssert.IsTrue(headerAfterPayloadWasUploaded.FileMetadata.Payloads.Count() == 1);
        var thePayloadDescriptor = headerAfterPayloadWasUploaded.FileMetadata.Payloads.SingleOrDefault(p => p.KeyEquals(uploadedPayloadDefinition.Key));
        ClassicAssert.IsNotNull(thePayloadDescriptor);
        ClassicAssert.IsTrue(thePayloadDescriptor.ContentType == uploadedPayloadDefinition.ContentType);
        CollectionAssert.AreEquivalent(thePayloadDescriptor.Thumbnails, uploadedPayloadDefinition.Thumbnails);
        ClassicAssert.IsTrue(thePayloadDescriptor.BytesWritten == encryptedPayloadBytes.Length);

        // Last modified should be changed
        // ClassicAssert.IsTrue(thePayloadDescriptor.LastModified > headerBeforeUpload.FileMetadata.Updated);

        // Get the payload
        var getPayloadResponse = await _ownerApiClient.DriveRedux.GetPayload(targetFile, uploadedPayloadDefinition.Key);
        ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
        var encryptedPayloadContentBytes = await getPayloadResponse.Content.ReadAsByteArrayAsync();
        ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(encryptedPayloadContentBytes, encryptedPayloadBytes));

        var decryptedBytes = payloadKeyHeader.Decrypt(encryptedPayloadBytes);
        ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(decryptedBytes, uploadedPayloadDefinition.Content));

        return uploadPayloadResponse.Content!.NewVersionTag;
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