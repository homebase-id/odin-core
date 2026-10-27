#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Security.ZeroKnowledge;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Util;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Deposits on zero-knowledge drives (docs/zero-knowledge-recovery-plan.md, "Deposits"): a file whose
/// writer sealed its key header to the drive's write-only public key.  The server stores the sealed key
/// header as received, passes it on as is and returns it as stored; only the drive owner's browser opens
/// it.  On the drive it is a client-locked file, under the same rules as a zkDrive one
/// (docs/zero-knowledge-drive-api-plan.md, step 2).  Gated friend shards are the first deposits; these
/// tests use plain files.
/// </summary>
[TestFixture]
public class DepositTests : V2Fixture
{
    private const string Content = "only the drive's owner reads this";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task TheOwnerCannotUploadADepositToTheirOwnZeroKnowledgeDrive()
    {
        // The owner's devices hold the drive's key and write zkDrive; a deposit comes only from someone else
        var frodo = await LoginAsOwner(Identities.Frodo);
        await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        var publicKeyJwk = (await ZkOwnerSteps.GetShardRecoveryDriveAsync(frodo)).WriteOnlyPublicKeyJwk;
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);

        // The upload client throws on any 400 rather than returning it.
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DriveIsZeroKnowledge)));
    }

    [Test]
    public async Task ADepositIsRefusedOnADriveThatIsNotZeroKnowledge()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);

        // The upload client throws on any 400 rather than returning it.
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(Guid.NewGuid()),
            transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.DepositNeedsZeroKnowledgeDrive)));
    }

    [Test]
    public async Task ADepositCarriesNoPayloads()
    {
        // On the transient drive, where a deposit is written to be sent
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(ZkBrowser.MakeKeyPair().PublicKeyJwk);

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        var manifest = new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] };

        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias,
            Metadata(Guid.NewGuid()), transitOptions: null!, manifest, [payload], keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.InvalidPayload)));
    }

    [TestCase(true, TestName = "ADepositSentToAPeersZeroKnowledgeDriveArrivesSealed")]
    [TestCase(false, TestName = "ADepositSentToAPeersDriveThatIsNotZeroKnowledgeIsRefusedThere")]
    public async Task ADepositIsSentAsIs(bool recipientIsZeroKnowledge)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);

        var samsKey = recipientIsZeroKnowledge ? (await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword)).WriteOnlyPrivateKey : null;
        var samsPublicKey = await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam);
        Assert.That(samsPublicKey.IsZeroKnowledge, Is.EqualTo(recipientIsZeroKnowledge));

        // Written to Frodo's transient drive only to be sent: his server can't open it either
        var uniqueId = Guid.NewGuid();
        var (_, sealedKeyHeader) = await SendDepositAsync(frodo, frodo, sam, samsPublicKey.PublicKeyJwk, uniqueId);

        var received = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        if (!recipientIsZeroKnowledge)
        {
            Assert.That(received.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK), "a drive that isn't zero knowledge takes no deposit");
            return;
        }

        Assert.That(received.StatusCode, Is.EqualTo(HttpStatusCode.OK), received.Error?.Content);
        AssertOpens(received.Content!, samsKey!, sealedKeyHeader);
        Assert.That(received.Content!.FileMetadata.SenderOdinId, Is.EqualTo(frodo.Identity.DomainName));

        // A query needs no storage key for it: its key header goes back as stored
        var batch = await sam.Drives.Reader.GetBatchAsync(Drive.Alias, QueryFor(uniqueId));
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        AssertOpens(batch.Content!.SearchResults.Single(), samsKey!, sealedKeyHeader);
    }

    [Test]
    public async Task AReceivedDepositIsHiddenFromAnAppWithReadOnTheDrive()
    {
        // A client-locked file reaches only a caller holding the drive's key: an app holds none for a
        // zero-knowledge drive, and an old chat-kmp stalls its sync on a key header it can't open
        var (frodo, sam, samsKeys) = await ConnectedWithZeroKnowledgeSamAsync();
        var app = await AppSession.SetupAsync(sam, Drive, DrivePermission.Read);
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(frodo, frodo, sam, samsKeys.PublicKeyJwk, uniqueId);

        var owners = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(owners.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the owner sees it");

        var batch = await app.Drives.Reader.GetBatchAsync(Drive.Alias, QueryFor(uniqueId));
        Assert.That(batch.StatusCode, Is.EqualTo(HttpStatusCode.OK), batch.Error?.Content);
        Assert.That(batch.Content!.SearchResults, Is.Empty, "left out of the app's page");

        var byUniqueId = await app.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(byUniqueId.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), $"by unique id: {byUniqueId.Error?.Content}");
        var byFileId = await app.Drives.Reader.GetFileHeaderAsync(Drive.Alias, owners.Content!.FileId);
        Assert.That(byFileId.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), $"by file id: {byFileId.Error?.Content}");
    }

    [Test]
    public async Task AnAppSendsADepositThroughTheTransientDrive()
    {
        // The app reads back the deposit it staged there to send it; the read rule hides client-locked files
        // only off the transient drive
        var (frodo, sam, samsKeys) = await ConnectedWithZeroKnowledgeSamAsync();
        var app = await AppSession.SetupAsync(frodo, Drive, DrivePermission.Read, [PermissionKeys.UseTransitWrite]);
        var uniqueId = Guid.NewGuid();
        var (_, sealedKeyHeader) = await SendDepositAsync(app, frodo, sam, samsKeys.PublicKeyJwk, uniqueId);

        var received = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(received.StatusCode, Is.EqualTo(HttpStatusCode.OK), received.Error?.Content);
        AssertOpens(received.Content!, samsKeys.WriteOnlyPrivateKey, sealedKeyHeader);
    }

    [Test]
    public async Task AnAppWithWriteDepositsOnAZeroKnowledgeDriveButCannotReadItBack()
    {
        // Sealed: anyone with Write on the drive; only a caller holding the drive's key reads it
        var sam = await LoginAsOwner(Identities.Sam);
        var (_, samsKey, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var publicKeyJwk = (await ZkOwnerSteps.GetShardRecoveryDriveAsync(sam)).WriteOnlyPublicKeyJwk;
        var app = await AppSession.SetupAsync(sam, Drive, DrivePermission.ReadWrite);

        var uniqueId = Guid.NewGuid();
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var (upload, _, _, _) = await app.Drives.Writer.CreateEncryptedFile(Drive.Alias, Metadata(uniqueId), transitOptions: null!,
            keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);

        var appsRead = await app.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(appsRead.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), $"the app read it back: {appsRead.Error?.Content}");

        var ownersRead = await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias);
        Assert.That(ownersRead.StatusCode, Is.EqualTo(HttpStatusCode.OK), ownersRead.Error?.Content);
        AssertOpens(ownersRead.Content!, samsKey, sealedKeyHeader);
    }

    [Test]
    public async Task TheOwnerUpdatesADepositWithAZkDriveKeyHeader()
    {
        // A client-locked file is updated only by a caller holding the drive's key, with a zkDrive key header
        // and a new IV; the file key stays, and the deposit becomes the drive's own
        var (frodo, sam, samsKeys) = await ConnectedWithZeroKnowledgeSamAsync();
        var uniqueId = Guid.NewGuid();
        var (fileKey, _) = await SendDepositAsync(frodo, frodo, sam, samsKeys.PublicKeyJwk, uniqueId);
        var received = (await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias)).Content!;

        var newKey = WithNewIv(fileKey);
        var update = await UpdateAsync(sam, received.FileId, uniqueId, newKey, ZkBrowser.LockFileKey(newKey, samsKeys.DriveKey),
            received.FileMetadata.VersionTag);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);

        var read = (await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias)).Content!;
        Assert.That(read.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.ZkDrive));
        var opened = ZkBrowser.OpenFileKey(read.SharedSecretEncryptedKeyHeader, samsKeys.DriveKey);
        Assert.That(opened.Decrypt(read.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes(), Is.EqualTo("updated"));
    }

    [Test]
    public async Task ASealedKeyHeaderCannotUpdateAFile()
    {
        // A deposit is written whole: neither an update nor a metadata-only upload takes a sealed key header
        var (frodo, sam, samsKeys) = await ConnectedWithZeroKnowledgeSamAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(frodo, frodo, sam, samsKeys.PublicKeyJwk, uniqueId);
        var received = (await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias)).Content!;

        var (newKey, sealedAgain) = ZkBrowser.SealNewFileKey(samsKeys.PublicKeyJwk);
        var update = await UpdateAsync(sam, received.FileId, uniqueId, newKey, sealedAgain, received.FileMetadata.VersionTag);
        Assert.That(update.ErrorCode(), Is.EqualTo(OdinClientErrorCode.DepositCannotBeUpdated), update.Error?.Content);

        var metadata = Metadata(uniqueId);
        metadata.VersionTag = received.FileMetadata.VersionTag;
        var (metadataOnly, _) = await sam.V1.Drive.UpdateExistingEncryptedMetadata(
            new ExternalFileIdentifier { FileId = received.FileId, TargetDrive = Drive }, newKey, metadata, lockedKeyHeader: sealedAgain);
        Assert.That(metadataOnly.ErrorCode(), Is.EqualTo(OdinClientErrorCode.DepositCannotBeUpdated), metadataOnly.Error?.Content);
    }

    [Test]
    public async Task APayloadCannotBeAddedToADeposit()
    {
        var (frodo, sam, samsKeys) = await ConnectedWithZeroKnowledgeSamAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(frodo, frodo, sam, samsKeys.PublicKeyJwk, uniqueId);
        var received = (await sam.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, Drive.Alias)).Content!;

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        payload.Iv = ByteArrayUtil.GetRndByteArray(16);
        var upload = await sam.V1.Drive.UploadPayloads(new ExternalFileIdentifier { FileId = received.FileId, TargetDrive = Drive },
            received.FileMetadata.VersionTag, new UploadManifest { PayloadDescriptors = [payload.ToPayloadDescriptor()] }, [payload]);
        Assert.That(upload.ErrorCode(), Is.EqualTo(OdinClientErrorCode.InvalidPayload), upload.Error?.Content);
    }

    [Test]
    public async Task ADepositCannotReplaceAClientLockedFile()
    {
        // A sender can't replace a client-locked file in this step, by a full overwrite either; on the transient
        // drive, where the caller sees the staged deposit, the overwrite is refused rather than missed
        var frodo = await LoginAsOwner(Identities.Frodo);
        var publicKeyJwk = ZkBrowser.MakeKeyPair().PublicKeyJwk;
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var (staged, _, _, _) = await frodo.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias,
            Metadata(Guid.NewGuid()), transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(staged.StatusCode, Is.EqualTo(HttpStatusCode.OK), staged.Error?.Content);

        var (otherKey, otherSealed) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var replacement = Metadata(Guid.NewGuid());
        replacement.VersionTag = staged.Content!.NewVersionTag;
        var overwrite = await frodo.V1.Drive.OverwriteWithLockedKeyHeader(SystemDriveConstants.TransientTempDrive, replacement,
            staged.Content.FileId, otherKey, otherSealed);
        Assert.That(overwrite.ErrorCode(), Is.EqualTo(OdinClientErrorCode.DepositCannotBeUpdated), overwrite.Error?.Content);
    }

    private sealed record SamsKeys(byte[] WriteOnlyPrivateKey, byte[] DriveKey, string PublicKeyJwk);

    private async Task<(OwnerSession Frodo, OwnerSession Sam, SamsKeys SamsKeys)> ConnectedWithZeroKnowledgeSamAsync()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        var (_, writeOnlyPrivateKey, driveKey) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(sam, OwnerLogin.DefaultPassword);
        var publicKey = await ZkFriends.GetShardRecoveryPublicKeyAsync(frodo, sam);
        return (frodo, sam, new SamsKeys(writeOnlyPrivateKey, driveKey, publicKey.PublicKeyJwk));
    }

    /// <summary>
    /// <paramref name="sender"/> (Frodo, or an app of his) writes a deposit to his transient drive and sends it to
    /// Sam's Shard Recovery drive.
    /// </summary>
    private static async Task<(KeyHeader FileKey, EncryptedKeyHeader Sealed)> SendDepositAsync(IV2Caller sender,
        OwnerSession frodo, OwnerSession sam, string publicKeyJwk, Guid uniqueId)
    {
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);
        var metadata = Metadata(uniqueId);
        metadata.AllowDistribution = true;
        var (upload, _, _, _) = await sender.Drives.Writer.CreateEncryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
            new TransitOptions
            {
                IsTransient = true,
                Recipients = [sam.Identity.DomainName],
                RemoteTargetDrive = Drive
            }, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        await frodo.Sync.DrainOutboxAsync();
        return (fileKey, sealedKeyHeader);
    }

    private static UploadFileMetadata Metadata(Guid uniqueId) => ZkTestFiles.Metadata(uniqueId, Content);

    /// <summary>The key header came back as it was sent, and the drive's write-only private key opens the content.</summary>
    private static void AssertOpens(SharedSecretEncryptedFileHeader header, byte[] writeOnlyPrivateKey, EncryptedKeyHeader sent)
    {
        Assert.That(header.SharedSecretEncryptedKeyHeader.EncryptedAesKey, Is.EqualTo(sent.EncryptedAesKey), "stored and returned as it came");
        Assert.That(ZkBrowser.OpenDeposit(header, writeOnlyPrivateKey).ToStringFromUtf8Bytes(), Is.EqualTo(Content));
    }
}
