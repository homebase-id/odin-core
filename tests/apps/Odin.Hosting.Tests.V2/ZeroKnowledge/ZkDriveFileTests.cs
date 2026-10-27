#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A zero-knowledge drive's own files (docs/zero-knowledge-drive-api-plan.md, step 1): an owner device
/// locks each key header with the drive key (<see cref="EncryptionType.ZkDrive"/>), which the server
/// doesn't hold.  The server stores it as sent, returns it as stored, and refuses a key header of
/// the wrong type without opening it.  The test plays the owner's device with <see cref="ZkBrowser"/>.
/// </summary>
[TestFixture]
public class ZkDriveFileTests : V2Fixture
{
    private const string Content = "only the owner's devices read this";

    private static readonly TargetDrive Drive = ZkTestFiles.Drive;

    [Test]
    public async Task AZkDriveFileIsStoredAndReturnedAsSentAndTheDriveKeyOpensIt()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var uniqueId = Guid.NewGuid();
        var (_, _, locked) = await UploadAsync(frodo, driveKey, uniqueId);

        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        AssertOpens(read.Content!, driveKey, locked, Content);

        var batch = await frodo.Drives.Reader.GetBatchAsync(Drive.Alias, QueryFor(uniqueId));
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        AssertOpens(batch.Content!.SearchResults.Single(), driveKey, locked, Content);
    }

    [TestCase(false, TestName = "AnAppThatCannotOpenItDoesNotSeeIt")]
    [TestCase(true, TestName = "AnAppHoldingTheDriveKeyFromBeforeTheConversionDoesNotSeeItEither")]
    public async Task AnAppThatCannotOpenItDoesNotSeeIt(bool grantedBeforeTheConversion)
    {
        // An app with read on the drive holds no key for it, and an app that can't open a key header must
        // not be handed one: chat-kmp would stall its sync on it.  A grant made before the conversion still
        // carries the old drive key, which opens nothing here.
        var frodo = await LoginAsOwner(Identities.Frodo);
        var earlyApp = grantedBeforeTheConversion ? await AppSession.SetupAsync(frodo, Drive, DrivePermission.Read) : null;
        var (_, driveKey) = await ZeroKnowledgeDriveAsync(frodo);
        var app = earlyApp ?? await AppSession.SetupAsync(frodo, Drive, DrivePermission.Read);

        var uniqueId = Guid.NewGuid();
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var (upload, _, _) = await UploadAsync(frodo, driveKey, uniqueId, payload);

        // Left out of the page, not a failed page
        var batch = await app.Drives.Reader.GetBatchAsync(Drive.Alias, QueryFor(uniqueId));
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        Assert.That(batch.Content!.SearchResults, Is.Empty);

        // One read rule, each path's own answer: a read by unique id leaves it out as a query does, a read by id refuses
        var byUniqueId = await app.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(byUniqueId.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), $"a read by unique id: {byUniqueId.Error?.Content}");
        var byFileId = await app.Drives.Reader.GetFileHeaderAsync(Drive.Alias, upload.FileId);
        Assert.That(byFileId.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"a read by file id: {byFileId.Error?.Content}");
        var payloadRead = await app.Drives.Reader.GetPayloadAsync(Drive.Alias, upload.FileId, payload.Key);
        Assert.That(payloadRead.IsSuccessStatusCode, Is.False, "and a payload read");
    }

    [Test]
    public async Task AnAppCannotWriteAZkDriveKeyHeader()
    {
        // It holds no drive key, so whatever it sent would be junk the owner's devices can't open
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (_, driveKey) = await ZeroKnowledgeDriveAsync(frodo);
        var app = await AppSession.SetupAsync(frodo, Drive, DrivePermission.ReadWrite);

        var fileKey = KeyHeader.NewRandom16();
        var (upload, _, _, _) = await app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, driveKey));
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), upload.Error?.Content);
    }

    [Test]
    public async Task AKeyHeaderLockedWithTheSharedSecretIsRefusedWithoutBeingOpened()
    {
        var (frodo, _) = await ZeroKnowledgeDriveAsync();

        // Random bytes the shared secret can't open: a refusal after opening would be a decryption error
        var unopenable = new EncryptedKeyHeader
        {
            EncryptionVersion = 1,
            Type = EncryptionType.Aes,
            Iv = ByteArrayUtil.GetRndByteArray(16),
            EncryptedAesKey = ByteArrayUtil.GetRndByteArray(48)
        };

        // The upload client throws on any 400 rather than returning it.
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias,
            Metadata(Guid.NewGuid()), transitOptions: null!, keyHeader: KeyHeader.NewRandom16(), lockedKeyHeader: unopenable));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DriveIsZeroKnowledge)));
    }

    [Test]
    public async Task AZkDriveKeyHeaderIsRefusedOnADriveThatIsNotZeroKnowledge()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var normalDrive = TargetDrive.NewTargetDrive();
        await frodo.Admin.EnsureDrive(normalDrive, "not zero knowledge", allowAnonymousReads: false);

        var fileKey = KeyHeader.NewRandom16();
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(normalDrive.Alias,
            Metadata(Guid.NewGuid()), transitOptions: null!, keyHeader: fileKey,
            lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, ByteArrayUtil.GetRndByteArray(16))));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.ZkDriveKeyHeaderNeedsZeroKnowledgeDrive)));
    }

    [TestCase("short key", TestName = "AZkDriveKeyHeaderWithAShortKeyIsRefused")]
    [TestCase("weak iv", TestName = "AZkDriveKeyHeaderWithAWeakIvIsRefused")]
    [TestCase("version 2", TestName = "AZkDriveKeyHeaderOfAnUnknownVersionIsRefused")]
    public async Task AMalformedZkDriveKeyHeaderIsRefused(string fault)
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var fileKey = KeyHeader.NewRandom16();
        var locked = ZkBrowser.LockFileKey(fileKey, driveKey);
        switch (fault)
        {
            case "short key":
                locked.EncryptedAesKey = locked.EncryptedAesKey[..32];
                break;
            case "weak iv":
                locked.Iv = new byte[16];
                break;
            case "version 2":
                locked.EncryptionVersion = 2;
                break;
        }

        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias,
            Metadata(Guid.NewGuid()), transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: locked));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.InvalidKeyHeader)));
    }

    [Test]
    public async Task AMetadataOnlyUpdateStoresTheNewKeyHeaderAsSentAndMustChangeTheIv()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var uniqueId = Guid.NewGuid();
        var (upload, fileKey, _) = await UploadAsync(frodo, driveKey, uniqueId);
        var file = new ExternalFileIdentifier { TargetDrive = Drive, FileId = upload.FileId };

        // Same file key, new IV, as a device updates it
        var newKeyHeader = WithNewIv(fileKey);
        var newLocked = ZkBrowser.LockFileKey(newKeyHeader, driveKey);
        var (update, _) = await frodo.V1.Drive.UpdateExistingEncryptedMetadata(file, newKeyHeader,
            Metadata(uniqueId, "updated", upload.NewVersionTag), lockedKeyHeader: newLocked);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);

        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        AssertOpens(read.Content!, driveKey, newLocked, "updated");

        var (again, _) = await frodo.V1.Drive.UpdateExistingEncryptedMetadata(file, newKeyHeader,
            Metadata(uniqueId, "again", read.Content!.FileMetadata.VersionTag), lockedKeyHeader: newLocked);
        Assert.That(again.ErrorCode(), Is.EqualTo(OdinClientErrorCode.MustRotateKeyHeaderIvWhenUpdating), again.Error?.Content);
    }

    [Test]
    public async Task AnUpdateStoresTheNewKeyHeaderAsSentAndMustChangeTheIv()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var uniqueId = Guid.NewGuid();
        var (upload, fileKey, _) = await UploadAsync(frodo, driveKey, uniqueId);

        var newKeyHeader = WithNewIv(fileKey);
        var newLocked = ZkBrowser.LockFileKey(newKeyHeader, driveKey);
        var update = await UpdateAsync(frodo, upload.FileId, uniqueId, newKeyHeader, newLocked, upload.NewVersionTag);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);

        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        AssertOpens(read.Content!, driveKey, newLocked, "updated");

        var again = await UpdateAsync(frodo, upload.FileId, uniqueId, newKeyHeader, newLocked, update.Content!.NewVersionTag);
        Assert.That(again.ErrorCode(), Is.EqualTo(OdinClientErrorCode.MustRotateKeyHeaderIvWhenUpdating), again.Error?.Content);

        // A key header locked with the shared secret is refused here too, unopened
        var sharedSecret = frodo.SharedSecret;
        var aes = EncryptedKeyHeader.EncryptKeyHeaderAes(KeyHeader.NewRandom16(), ByteArrayUtil.GetRndByteArray(16), ref sharedSecret);
        var refused = await UpdateAsync(frodo, upload.FileId, uniqueId, KeyHeader.NewRandom16(), aes, update.Content!.NewVersionTag);
        Assert.That(refused.ErrorCode(), Is.EqualTo(OdinClientErrorCode.DriveIsZeroKnowledge), refused.Error?.Content);
    }

    [Test]
    public async Task AnAppCannotOverwriteOrDeleteAZkDriveFile()
    {
        // Not only its key header: the file is the owner console's alone, to read or to change
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (_, driveKey) = await ZeroKnowledgeDriveAsync(frodo);
        var app = await AppSession.SetupAsync(frodo, Drive, DrivePermission.ReadWrite);
        var uniqueId = Guid.NewGuid();
        var (upload, _, locked) = await UploadAsync(frodo, driveKey, uniqueId);

        var plaintext = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.OwnerOnly);
        plaintext.VersionTag = upload.NewVersionTag;
        var overwrite = await app.V1.Drive.UploadNewMetadata(Drive, plaintext, new TransitOptions(), overwriteFileId: upload.FileId);
        Assert.That(overwrite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"an overwrite with plaintext: {overwrite.Error?.Content}");

        var delete = await app.Drives.Writer.SoftDeleteFile(Drive.Alias, upload.FileId);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"a delete: {delete.Error?.Content}");

        // A batch delete reads the file as missing when the caller may not see it
        var batch = await app.Drives.Writer.SoftDeleteBatch(Drive.Alias,
            new SoftDeleteBatchRequestV2 { Files = [new SoftDeleteFileV2 { FileId = upload.FileId, Recipients = [] }] });
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        Assert.That(batch.Content!.Results.Single().ErrorCode, Is.EqualTo(OdinClientErrorCode.FileNotFound), "a batch delete");

        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        AssertOpens(read.Content!, driveKey, locked, Content);
    }

    [Test]
    public async Task ADeletedZkDriveFileStaysHiddenFromApps()
    {
        // Its tombstone keeps the zkDrive key header but not IsEncrypted; the socket for the delete hides it too
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (_, driveKey) = await ZeroKnowledgeDriveAsync(frodo);
        var app = await AppSession.SetupAsync(frodo, Drive, DrivePermission.Read);
        var (upload, _, _) = await UploadAsync(frodo, driveKey, Guid.NewGuid());

        var delete = await frodo.Drives.Writer.SoftDeleteFile(Drive.Alias, upload.FileId);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);

        var read = await app.Drives.Reader.GetFileHeaderAsync(Drive.Alias, upload.FileId);
        Assert.That(read.IsSuccessStatusCode, Is.False, $"the tombstone reached the app: {read.StatusCode}");
    }

    [Test]
    public async Task TheCdnNeverServesAZkDriveFile()
    {
        // The CDN reads as the system, which reaches a drive's own files for its jobs; a zero-knowledge drive
        // is never one of the CDN's drives, even when the owner turns CDN on for it
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        await frodo.Admin.SetAllowCdn(Drive, true);
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var (upload, _, _) = await UploadAsync(frodo, driveKey, Guid.NewGuid(), payload);

        var cdn = CdnSession.Setup(Host, Identities.Frodo);
        var read = await cdn.Drives.Reader.GetPayloadAsync(Drive.Alias, upload.FileId, payload.Key);
        Assert.That(read.IsSuccessStatusCode, Is.False, $"the CDN served it: {read.StatusCode}");
    }

    [Test]
    public async Task AnUnencryptedFileWithRecipientsIsRefusedBeforeItIsStored()
    {
        // A zero-knowledge drive sends nothing, encrypted or not
        var (frodo, _) = await ZeroKnowledgeDriveAsync();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.OwnerOnly, allowDistribution: true);
        metadata.AppData.UniqueId = Guid.NewGuid();

        var upload = await frodo.V1.Drive.UploadNewMetadata(Drive, metadata, new TransitOptions { Recipients = [Identities.Sam] });
        Assert.That(upload.ErrorCode(), Is.EqualTo(OdinClientErrorCode.ZkDriveFileCannotBeSent), upload.Error?.Content);

        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(metadata.AppData.UniqueId.Value, Drive.Alias);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "nothing was stored");
    }

    [Test]
    public async Task AMetadataOnlyUploadWithRecipientsIsRefusedBeforeAnythingIsStored()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var uniqueId = Guid.NewGuid();
        var (upload, fileKey, locked) = await UploadAsync(frodo, driveKey, uniqueId);

        var newKeyHeader = WithNewIv(fileKey);
        var metadata = Metadata(uniqueId, "updated", upload.NewVersionTag);
        metadata.AllowDistribution = true;
        var (update, _) = await frodo.V1.Drive.UpdateExistingEncryptedMetadata(
            new ExternalFileIdentifier { TargetDrive = Drive, FileId = upload.FileId }, newKeyHeader, metadata,
            lockedKeyHeader: ZkBrowser.LockFileKey(newKeyHeader, driveKey),
            transitOptions: new TransitOptions { Recipients = [Identities.Sam] });
        Assert.That(update.ErrorCode(), Is.EqualTo(OdinClientErrorCode.ZkDriveFileCannotBeSent), update.Error?.Content);

        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(read.Content!.FileMetadata.VersionTag, Is.EqualTo(upload.NewVersionTag), "nothing was stored");
        AssertOpens(read.Content!, driveKey, locked, Content);
    }

    [Test]
    public async Task AnUpdateWithRecipientsIsRefused()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var uniqueId = Guid.NewGuid();
        var (upload, fileKey, _) = await UploadAsync(frodo, driveKey, uniqueId);

        var newKeyHeader = WithNewIv(fileKey);
        var update = await UpdateAsync(frodo, upload.FileId, uniqueId, newKeyHeader, ZkBrowser.LockFileKey(newKeyHeader, driveKey),
            upload.NewVersionTag, recipients: [(OdinId)Identities.Sam]);
        Assert.That(update.ErrorCode(), Is.EqualTo(OdinClientErrorCode.ZkDriveFileCannotBeSent), update.Error?.Content);
    }

    [Test]
    public async Task AnUnencryptedUpdateOfAZkDriveFileIsRefusedAsAChangeOfEncryption()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var uniqueId = Guid.NewGuid();
        var (upload, _, _) = await UploadAsync(frodo, driveKey, uniqueId);

        var metadata = Metadata(uniqueId, "in the clear", upload.NewVersionTag);
        metadata.IsEncrypted = false;
        var update = await frodo.Drives.Writer.UpdateFileByFileId(Drive.Alias, upload.FileId, new FileUpdateInstructionSetV2
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            Manifest = new UploadManifest { PayloadDescriptors = [] }
        }, metadata, []);
        Assert.That(update.ErrorCode(), Is.EqualTo(OdinClientErrorCode.ArgumentError), update.Error?.Content);
    }

    [Test]
    public async Task ConvertingTheDriveAgainDeletesTheFilesTheOldDriveKeyLocked()
    {
        // The browser converts again only when the old drive key no longer opens (a reset forgot it), so
        // those files can't be opened by anyone
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (zkMasterKey, _, driveKey) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        var uniqueId = Guid.NewGuid();
        await UploadAsync(frodo, driveKey, uniqueId);

        await ZkOwnerSteps.ConvertShardRecoveryDriveAsync(frodo, zkMasterKey);

        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), read.Error?.Content);
    }

    [Test]
    public async Task AnUnencryptedFileOnAZeroKnowledgeDriveCanBeOverwritten()
    {
        // An unencrypted file has no key header to lock, as for a new one
        var (frodo, _) = await ZeroKnowledgeDriveAsync();
        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.OwnerOnly);
        var upload = await frodo.V1.Drive.UploadNewMetadata(Drive, metadata, new TransitOptions());
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var overwrite = SampleMetadataData.Create(fileType: 101, acl: AccessControlList.OwnerOnly);
        overwrite.VersionTag = upload.Content!.NewVersionTag;
        var response = await frodo.V1.Drive.UploadNewMetadata(Drive, overwrite, new TransitOptions(),
            overwriteFileId: upload.Content.File.FileId);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);
    }

    [Test]
    public async Task APayloadAndThumbnailComeWithoutAKeyHeaderAndOpenWithTheFileKeyAndTheirIv()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var payload = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var (upload, fileKey, _) = await UploadAsync(frodo, driveKey, Guid.NewGuid(), payload);
        var payloadKey = new KeyHeader { Iv = payload.Iv, AesKey = fileKey.AesKey };

        var whole = await frodo.Drives.Reader.GetPayloadAsync(Drive.Alias, upload.FileId, payload.Key);
        Assert.That(whole.StatusCode, Is.EqualTo(HttpStatusCode.OK), whole.Error?.Content);
        AssertNoKeyHeader(whole);
        var cipher = await whole.Content!.ReadAsByteArrayAsync();
        Assert.That(payloadKey.Decrypt(cipher), Is.EqualTo(payload.Content));

        // The server never decrypts content, so a range is the stored bytes
        var range = await frodo.Drives.Reader.GetPayloadAsync(Drive.Alias, upload.FileId, payload.Key,
            new FileChunk { Start = 16, Length = 16 });
        Assert.That(range.IsSuccessStatusCode, Is.True, range.Error?.Content);
        AssertNoKeyHeader(range);
        Assert.That(await range.Content!.ReadAsByteArrayAsync(), Is.EqualTo(cipher[16..32]));

        var thumbnail = payload.Thumbnails.Single();
        var thumb = await frodo.Drives.Reader.GetThumbnailAsync(Drive.Alias, upload.FileId, payload.Key,
            thumbnail.PixelWidth, thumbnail.PixelHeight);
        Assert.That(thumb.StatusCode, Is.EqualTo(HttpStatusCode.OK), thumb.Error?.Content);
        AssertNoKeyHeader(thumb);
        Assert.That(payloadKey.Decrypt(await thumb.Content!.ReadAsByteArrayAsync()), Is.EqualTo(thumbnail.Content));
    }

    [Test]
    public async Task AZkDriveFileIsNotSent()
    {
        var (frodo, driveKey) = await ZeroKnowledgeDriveAsync();
        var fileKey = KeyHeader.NewRandom16();
        var metadata = Metadata(Guid.NewGuid());
        metadata.AllowDistribution = true;

        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias, metadata,
            new TransitOptions { Recipients = [Identities.Sam] }, keyHeader: fileKey,
            lockedKeyHeader: ZkBrowser.LockFileKey(fileKey, driveKey)));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.ZkDriveFileCannotBeSent)));
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>Frodo's Shard Recovery drive made zero knowledge, and the drive key his device then holds.</summary>
    private async Task<(OwnerSession Owner, byte[] DriveKey)> ZeroKnowledgeDriveAsync(OwnerSession? owner = null)
    {
        var frodo = owner ?? await LoginAsOwner(Identities.Frodo);
        var (_, _, driveKey) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        return (frodo, driveKey);
    }

    /// <summary>The same file key with a new IV, as a device updates a file.</summary>
    private static async Task<(CreateFileResult File, KeyHeader FileKey, EncryptedKeyHeader Locked)> UploadAsync(OwnerSession owner,
        byte[] driveKey, Guid uniqueId, TestPayloadDefinition? payload = null)
    {
        var fileKey = KeyHeader.NewRandom16();
        var locked = ZkBrowser.LockFileKey(fileKey, driveKey);

        UploadManifest? manifest = null;
        if (payload != null)
        {
            payload.Iv = ByteArrayUtil.GetRndByteArray(16);
            manifest = new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] };
        }

        var (upload, _, _, _) = await owner.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(uniqueId), transitOptions: null!,
            manifest, payload == null ? null : [payload], keyHeader: fileKey, lockedKeyHeader: locked);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        return (upload.Content!, fileKey, locked);
    }

    private static UploadFileMetadata Metadata(Guid uniqueId, string content = Content, Guid? versionTag = null) =>
        ZkTestFiles.Metadata(uniqueId, content, versionTag);

    /// <summary>The key header came back as it was sent, and the drive key opens the content.</summary>
    private static void AssertOpens(SharedSecretEncryptedFileHeader header, byte[] driveKey, EncryptedKeyHeader sent, string content)
    {
        var returned = header.SharedSecretEncryptedKeyHeader;
        Assert.That(returned.Iv, Is.EqualTo(sent.Iv), "stored and returned as it came");
        Assert.That(returned.EncryptedAesKey, Is.EqualTo(sent.EncryptedAesKey), "stored and returned as it came");
        var fileKey = ZkBrowser.OpenFileKey(returned, driveKey);
        Assert.That(fileKey.Decrypt(header.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes(), Is.EqualTo(content));
    }

    private static void AssertNoKeyHeader(Refit.IApiResponse response) =>
        Assert.That(response.Headers.Contains(HttpHeaderConstants.SharedSecretEncryptedKeyHeader64), Is.False,
            "the client opens it with the file key and the payload's IV");
}
