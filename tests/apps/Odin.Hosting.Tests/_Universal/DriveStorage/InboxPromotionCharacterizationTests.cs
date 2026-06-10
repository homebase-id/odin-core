using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.DriveStorage;

/// <summary>
/// Characterization tests that lock the behavior of the peer-transfer commit/promote
/// pipeline on disk storage. These tests must continue to pass unchanged through the
/// storage-backend unification refactor (Phase 1 safety net).
/// </summary>
public class InboxPromotionCharacterizationTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>()
        {
            TestIdentities.Frodo,
            TestIdentities.Samwise
        });
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
    /// Verifies the full stage-to-long-term promotion path for a peer transfer that
    /// carries a payload and a thumbnail. The recipient's promoted copy must be
    /// byte-for-byte identical (after decryption) to what the sender uploaded.
    ///
    /// The file is uploaded ENCRYPTED so the transfer routes through the inbox
    /// (CanDirectWriteFile returns false when IsEncrypted=true and the caller lacks
    /// the recipient's drive storage key inline). The "zero results before ProcessInbox"
    /// assertion proves the inbox path was exercised rather than direct-write.
    ///
    /// This is the behavioral oracle for the storage-backend unification refactor:
    /// it must pass on disk (the current default) and continue to pass after each
    /// refactor phase.
    /// </summary>
    [Test]
    public async Task PeerTransfer_WithPayloadAndThumbnail_CommitsToLongTerm_Disk()
    {
        await InboxPromotionScenario.AssertEncryptedPeerTransferPromotesPayloadAndThumbnail(_scaffold);
    }

    /// <summary>
    /// Verifies the full stage-to-long-term promotion path for a plain local upload that
    /// carries a payload and a thumbnail. The owner's promoted copy must be byte-for-byte
    /// identical to what was uploaded (no encryption, so no decryption step needed).
    ///
    /// This test characterizes the Upload staging area branch of CommitNewFile/
    /// CopyPayloadsAndThumbnailsToLongTermStorage. It is the behavioral oracle for the
    /// storage-backend unification refactor: the Upload branch must continue to work
    /// after each refactor phase (Phase 1 safety net).
    /// </summary>
    [Test]
    public async Task LocalUpload_WithPayloadAndThumbnail_CommitsToLongTerm_Disk()
    {
        var ownerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var targetDrive = TargetDrive.NewTargetDrive();

        // 1. Create the target drive.
        var createDriveResponse = await ownerClient.DriveManager.CreateDrive(
            targetDrive: targetDrive,
            name: "Local upload characterization drive",
            metadata: "",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);
        ClassicAssert.IsTrue(createDriveResponse.IsSuccessStatusCode);

        // 2. Build an unencrypted upload with one payload + one thumbnail.
        var payloadDef = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var testPayloads = new List<TestPayloadDefinition> { payloadDef };

        var uploadManifest = new UploadManifest
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var fileMetadata = SampleMetadataData.Create(fileType: 4343);

        // 3. Upload via the owner drive redux helper (plain local upload, no TransitOptions).
        var uploadResponse = await ownerClient.DriveRedux.UploadNewFile(
            targetDrive, fileMetadata, uploadManifest, testPayloads);

        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode,
            $"Upload failed: {uploadResponse.StatusCode}");
        var uploadResult = uploadResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // 4. Retrieve the file header and confirm payload metadata is present.
        var getHeaderResponse = await ownerClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
        var header = getHeaderResponse.Content;
        ClassicAssert.IsNotNull(header);
        ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(),
            "File header should list at least one payload after promotion");

        // 5. Download the payload and assert byte-equality (no decryption: upload is unencrypted).
        var getPayloadResponse = await ownerClient.DriveRedux.GetPayload(uploadResult.File, payloadDef.Key);
        ClassicAssert.IsTrue(getPayloadResponse.IsSuccessStatusCode,
            $"GetPayload returned {getPayloadResponse.StatusCode}");

        var payloadBytes = (await getPayloadResponse.Content.ReadAsStreamAsync()).ToByteArray();
        CollectionAssert.AreEqual(payloadDef.Content, payloadBytes,
            "Payload bytes must be byte-equal to what was uploaded");

        // 6. Download the thumbnail and assert byte-equality.
        ClassicAssert.IsTrue(
            header.FileMetadata.Payloads.Any(p => p.Thumbnails.Any()),
            "File header should list at least one thumbnail under the payload");

        var thumbnail = payloadDef.Thumbnails.First();
        var getThumbnailResponse = await ownerClient.DriveRedux.GetThumbnail(
            uploadResult.File, thumbnail.PixelWidth, thumbnail.PixelHeight, payloadDef.Key);
        ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode,
            $"GetThumbnail returned {getThumbnailResponse.StatusCode}");

        var thumbnailBytes = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
        CollectionAssert.AreEqual(thumbnail.Content, thumbnailBytes,
            "Thumbnail bytes must be byte-equal to what was uploaded");
    }
}
