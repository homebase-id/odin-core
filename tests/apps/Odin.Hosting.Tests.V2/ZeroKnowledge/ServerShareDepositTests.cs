#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Cryptography.Crypto;
using Odin.Core.Exceptions;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A Server Share drive takes a peer's sealed file at once, without the inbox, and its server makes it its own file on
/// the first read whose request carries the drive key (docs/server-share-sealed-arrivals-plan.md, step 1); a sender may
/// still change it with a sealed key header, or delete it, while no one else has (step 1b).
/// </summary>
[TestFixture]
public class ServerShareDepositTests : V2Fixture
{
    private const string Content = "Sam's, for Frodo's shared drive";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task APeersSealedFileIsWrittenAtOnceAndTheServerMakesItItsOwnOnRead()
    {
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, remoteDrive: drive);

        Assert.That(await InboxCountAsync(frodo, drive), Is.EqualTo(0), "written at once, not queued for a key holder");
        var stored = (await StoredKeyHeadersAsync(frodo, drive)).Single();
        Assert.That(stored.KeyHeader.Type, Is.EqualTo(EncryptionType.Sealed), "stored as sent until read");

        var read = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(ContentAsOwner(frodo, read), Is.EqualTo(Content), "the owner gets it as any file, locked with their shared secret");

        var after = (await StoredKeyHeadersAsync(frodo, drive)).Single();
        Assert.That(after.KeyHeader.Type, Is.EqualTo(EncryptionType.Aes), "now the server's own key header");
        Assert.That(after.VersionTag, Is.EqualTo(stored.VersionTag), "the version tag doesn't move");
        Assert.That(after.KeyHeader.Iv, Is.EqualTo(stored.KeyHeader.Iv), "under the IV the content was encrypted with");

        var again = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(again.FileMetadata.Updated, Is.EqualTo(read.FileMetadata.Updated), "nor modified");
    }

    [Test]
    public async Task AnAppsFirstReadByIdGetsTheDepositAsAnyFile()
    {
        // Converted before the read rule, which shows a sealed file only to a key holder of a zero-knowledge drive: an app
        // reading the drive gets it, by the by-id path, before any other read has converted it
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.ReadWrite);
        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, Guid.NewGuid(), Content, remoteDrive: drive);
        var fileId = (await StoredKeyHeadersAsync(frodo, drive)).Single().FileId;

        var read = await app.Drives.Reader.GetFileHeaderAsync(drive.Alias, fileId);

        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        Assert.That(read.Content!.SharedSecretEncryptedKeyHeader.Type, Is.EqualTo(EncryptionType.Aes), "locked with its shared secret");
        Assert.That((await StoredKeyHeadersAsync(frodo, drive)).Single().KeyHeader.Type, Is.EqualTo(EncryptionType.Aes), "and stored so");
    }

    [Test]
    public async Task AnAppDeletesADepositNoReadConvertedAndGetsItsTombstone()
    {
        // A delete doesn't convert, and the tombstone keeps the sealed key header: a sealed key header on a Server Share
        // drive reaches whoever holds the drive's storage key (ZkDriveFiles.VisibleTo), as an app learning of the deposit
        // from a socket event, which opens it without writing it, does
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var app = await AppSession.SetupAsync(frodo, drive, DrivePermission.ReadWrite);
        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, Guid.NewGuid(), Content, remoteDrive: drive);
        var fileId = (await StoredKeyHeadersAsync(frodo, drive)).Single().FileId;

        // The batch, whose load passes includeExpired (the single delete loads it as a write, which converts)
        var delete = await app.Drives.Writer.SoftDeleteBatch(drive.Alias, new SoftDeleteBatchRequestV2 { Files = [new SoftDeleteFileV2 { FileId = fileId }] });
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        Assert.That(delete.Content!.Results.Single().ErrorCode, Is.Null, "the app may delete it");

        var tombstone = await app.Drives.Reader.GetFileHeaderAsync(drive.Alias, fileId);
        Assert.That(tombstone.StatusCode, Is.EqualTo(HttpStatusCode.OK), tombstone.Error?.Content);
        Assert.That(tombstone.Content!.FileState, Is.EqualTo(Odin.Services.Drives.DriveCore.Storage.FileState.Deleted));
        Assert.That((tombstone.Content.SharedSecretEncryptedKeyHeader.Type, tombstone.Content.SharedSecretEncryptedKeyHeader.Iv),
            Is.EqualTo((EncryptionType.Aes, Guid.Empty.ToByteArray())), "empty, as any tombstone's: it has no content to open");
        Assert.That((await StoredKeyHeadersAsync(frodo, drive)).Single().KeyHeader.Type, Is.EqualTo(EncryptionType.Sealed),
            "the delete didn't convert it");
    }

    [Test]
    public async Task ADepositSealedToAnotherKeyIsRefused()
    {
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();

        await SendDepositAsync(sam, sam, frodo, ZkBrowser.MakeKeyPair().PublicKeyJwk, Guid.NewGuid(), Content, remoteDrive: drive);

        Assert.That(await StoredKeyHeadersAsync(frodo, drive), Is.Empty, "the server could never open it");
        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, Guid.NewGuid(), Content, remoteDrive: drive);
        Assert.That(await StoredKeyHeadersAsync(frodo, drive), Has.Count.EqualTo(1), "the control: sealed to the drive's key, it lands");
    }

    [Test]
    public async Task ADepositOnAServerShareDriveCarriesItsPayloads()
    {
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var (fileKey, _) = await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, payload, remoteDrive: drive);

        var read = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        var bytes = await frodo.Drives.Reader.GetPayloadAsync(drive.Alias, read.FileId, payload.Key);
        Assert.That(bytes.StatusCode, Is.EqualTo(HttpStatusCode.OK), bytes.Error?.Content);
        var encrypted = await bytes.Content!.ReadAsByteArrayAsync();
        Assert.That(AesCbc.Decrypt(encrypted, fileKey.AesKey, payload.Iv), Is.EqualTo(payload.Content),
            "the sender's content, under the file key and the payload's IV");
    }

    [Test]
    public async Task TheOwnerCannotUploadADepositToTheirOwnServerShareDrive()
    {
        // A key holder writes a key header for the server to lock; a deposit comes only from someone without the drive key
        var (frodo, _, drive, publicKeyJwk) = await ArrangeAsync();
        var (fileKey, sealedKeyHeader) = ZkBrowser.SealNewFileKey(publicKeyJwk);

        // The upload client throws on any 400 rather than returning it.
        var e = Assert.ThrowsAsync<Exception>(() => frodo.Drives.Writer.CreateEncryptedFile(drive.Alias,
            Metadata(Guid.NewGuid(), Content), transitOptions: null!, keyHeader: fileKey, lockedKeyHeader: sealedKeyHeader));
        Assert.That(e!.Message, Does.EndWith(nameof(OdinClientErrorCode.InvalidKeyHeader)));
        Assert.That(await StoredKeyHeadersAsync(frodo, drive), Is.Empty);
    }

    [Test]
    public async Task ACommunityChannelTakesNoDeposit()
    {
        // It re-locks a member's post and sends it on as it lands, which needs the file's key then (FeedDriveDistributionRouter)
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync(communityChannel: true);

        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, Guid.NewGuid(), Content, remoteDrive: drive);

        Assert.That(await StoredKeyHeadersAsync(frodo, drive), Is.Empty, "refused on arrival");
        Assert.That(await InboxCountAsync(frodo, drive), Is.EqualTo(0));

        // The control: the same send, unsealed, lands, so the refusal was the deposit's.  Sam holds no key to the drive, so it
        // waits in the inbox for a key holder
        await SendUnsealedAsync(sam, frodo, drive, Guid.NewGuid(), Content);
        Assert.That(await InboxCountAsync(frodo, drive), Is.EqualTo(1), "through the inbox, as before");
        await frodo.Sync.ProcessInboxAsync(drive);
        Assert.That(await StoredKeyHeadersAsync(frodo, drive), Has.Count.EqualTo(1), "a member's post lands");
    }

    [Test]
    public async Task ASendersDeleteLandsAfterTheConversion()
    {
        // The conversion kept the version tag, so the sender's record of their last write still matches
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, remoteDrive: drive);
        var read = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That((await StoredKeyHeadersAsync(frodo, drive)).Single().KeyHeader.Type, Is.EqualTo(EncryptionType.Aes), "converted");

        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, read.FileMetadata.GlobalTransitId!.Value);
        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive);
        var deleted = await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, read.FileId);
        Assert.That(deleted.Content?.FileState, Is.EqualTo(Odin.Services.Drives.DriveCore.Storage.FileState.Deleted));
    }

    [Test]
    public async Task ASendersUnsealedChangeOfTheirDepositNoOneReadLands()
    {
        // Sent the old way, through the inbox, over a copy still sealed: Frodo's server, processing it, makes the deposit its
        // own first, as a read would (PeerFileWriter), and the change fits it as any server-locked file's
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var (fileKey, _) = await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, remoteDrive: drive);
        var stored = (await StoredKeyHeadersAsync(frodo, drive)).Single();
        Assert.That(stored.KeyHeader.Type, Is.EqualTo(EncryptionType.Sealed), "nobody has read it");

        await SendUnsealedAsync(sam, frodo, drive, uniqueId, "Sam's, changed", WithNewIv(fileKey), stored.VersionTag);
        await frodo.Sync.ProcessInboxAsync(drive);

        var changed = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(ContentAsOwner(frodo, changed), Is.EqualTo("Sam's, changed"));
    }

    [Test]
    public async Task ASendersSealedUpdateOfTheirConvertedCopyLandsUnderANewIv()
    {
        // An update of Frodo's copy only (UpdateBatchAsync): the server can't open the seal to compare file keys, so it
        // compares the IVs both key headers show
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var (fileKey, _) = await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, remoteDrive: drive);
        var read = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        var gtid = read.FileMetadata.GlobalTransitId!.Value;

        var sameIv = await UpdatePeerCopyAsync(sam, frodo, drive, gtid, uniqueId, "Sam's, same IV", fileKey,
            EncryptedKeyHeader.SealedTo(fileKey, publicKeyJwk));
        Assert.That(sameIv.StatusCode, Is.EqualTo(HttpStatusCode.OK), sameIv.Error?.Content);
        Assert.That((await HeaderByUniqueIdAsync(frodo, uniqueId, drive)).FileMetadata.VersionTag, Is.EqualTo(read.FileMetadata.VersionTag),
            "refused: the same key under the same IV");

        var newKey = WithNewIv(fileKey);
        var update = await UpdatePeerCopyAsync(sam, frodo, drive, gtid, uniqueId, "Sam's, updated", newKey,
            EncryptedKeyHeader.SealedTo(newKey, publicKeyJwk));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        Assert.That(ContentAsOwner(frodo, await HeaderByUniqueIdAsync(frodo, uniqueId, drive)), Is.EqualTo("Sam's, updated"));
    }

    [Test]
    public async Task ASendersUnsealedUpdateOfTheirStillSealedCopyLands()
    {
        // Sent the old way, through the inbox: the update's load makes the deposit the server's own first, and the file keys
        // then compare as any server-locked file's
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var (fileKey, _) = await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, remoteDrive: drive);
        var stored = (await StoredKeyHeadersAsync(frodo, drive)).Single();
        Assert.That(stored.KeyHeader.Type, Is.EqualTo(EncryptionType.Sealed), "nobody has read it");
        var gtid = (await Host.GetTenantScope(frodo.Identity.DomainName).Resolve<TableDriveMainIndexCached>()
            .GetAsync(drive.Alias, stored.FileId))!.globalTransitId!.Value;

        var newKey = WithNewIv(fileKey);
        var samsSecret = sam.SharedSecret;
        var update = await UpdatePeerCopyAsync(sam, frodo, drive, gtid, uniqueId, "Sam's, updated", newKey,
            EncryptedKeyHeader.EncryptKeyHeaderAes(newKey, newKey.Iv, ref samsSecret));
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        await frodo.Sync.ProcessInboxAsync(drive);

        Assert.That(ContentAsOwner(frodo, await HeaderByUniqueIdAsync(frodo, uniqueId, drive)), Is.EqualTo("Sam's, updated"));
    }

    [Test]
    public async Task ASendersSealedChangeAfterTheConversionLandsUntilSomeoneElseChangesTheFile()
    {
        // The conversion kept the version tag, so the sender's record of their last write still matches: their change is of
        // their own file, sealed and converted again (ZkDriveFiles.AssertMayReplace)
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, remoteDrive: drive);
        var read = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That((await StoredKeyHeadersAsync(frodo, drive)).Single().KeyHeader.Type, Is.EqualTo(EncryptionType.Aes), "converted");

        var (changedKey, _) = await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, "Sam's, changed", remoteDrive: drive,
            versionTag: read.FileMetadata.VersionTag);
        var changed = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(changed.FileMetadata.VersionTag, Is.Not.EqualTo(read.FileMetadata.VersionTag), "the change landed");
        Assert.That(ContentAsOwner(frodo, changed), Is.EqualTo("Sam's, changed"), "sealed again and converted again on the read");

        // Frodo changes it, as any update keeping its file key under a new IV: a seal of Sam's no longer replaces his write
        var frodosKey = WithNewIv(changedKey);
        var frodosSecret = frodo.SharedSecret;
        var update = await UpdateAsync(frodo, changed.FileId, uniqueId, frodosKey,
            EncryptedKeyHeader.EncryptKeyHeaderAes(frodosKey, frodosKey.Iv, ref frodosSecret),
            changed.FileMetadata.VersionTag, drive: drive);
        Assert.That(update.StatusCode, Is.EqualTo(HttpStatusCode.OK), update.Error?.Content);
        var frodos = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);

        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, "Sam's, again", remoteDrive: drive,
            versionTag: frodos.FileMetadata.VersionTag);
        Assert.That((await HeaderByUniqueIdAsync(frodo, uniqueId, drive)).FileMetadata.VersionTag,
            Is.EqualTo(frodos.FileMetadata.VersionTag), "refused: someone else changed the file since Sam's last write");
    }

    /// <summary>
    /// Sam updates Frodo's copy only, by its global transit id, naming no tag (a sender's change of their own file), with
    /// <paramref name="content"/> encrypted under <paramref name="fileKey"/> and <paramref name="locked"/> as its key header.
    /// </summary>
    private static async Task<Refit.ApiResponse<UpdateFileResult>> UpdatePeerCopyAsync(OwnerSession sam, OwnerSession frodo,
        TargetDrive drive, Guid globalTransitId, Guid uniqueId, string content, KeyHeader fileKey, EncryptedKeyHeader locked)
    {
        var metadata = Metadata(uniqueId, content);
        metadata.AppData.Content = fileKey.EncryptDataAes(content.ToUtf8ByteArray()).ToBase64();
        metadata.IsEncrypted = true;
        metadata.AllowDistribution = true;
        var update = await sam.Drives.Writer.UpdatePeerFileByGlobalTransitId(frodo.Identity, drive.Alias, globalTransitId,
            new FileUpdateInstructionSetV2
            {
                TransferIv = ByteArrayUtil.GetRndByteArray(16),
                Manifest = new UploadManifest { PayloadDescriptors = [] }
            }, metadata, [], locked);
        await sam.Sync.DrainOutboxAsync();
        return update;
    }

    private Task<int> InboxCountAsync(OwnerSession owner, TargetDrive drive) =>
        Host.GetTenantScope(owner.Identity.DomainName).Resolve<TableInbox>().GetReadyCountAsync(drive.Alias);

    private async Task<List<(Guid FileId, Guid VersionTag, EncryptedKeyHeader KeyHeader)>> StoredKeyHeadersAsync(OwnerSession owner,
        TargetDrive drive) =>
        (await Host.GetTenantScope(owner.Identity.DomainName).Resolve<TableDriveMainIndexCached>().GetKeyHeadersByDriveIdAsync(drive.Alias))
        .Select(r => (r.FileId, r.VersionTag, OdinSystemSerializer.Deserialize<EncryptedKeyHeader>(r.EncryptedKeyHeader)!))
        .ToList();

    /// <summary>Frodo's Server Share drive, on which Sam may write but not read, and its write-only public key.</summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam, TargetDrive Drive, string PublicKeyJwk)> ArrangeAsync(
        bool communityChannel = false)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var drive = await CreateServerShareDriveAsync(frodo, communityChannel);
        await PeerFlow.ConnectAsync(sam, frodo, drive, DrivePermission.Write);
        var publicKeyJwk = (await frodo.Admin.GetDrive(drive)).WriteOnlyPublicKeyJwk;
        Assert.That(publicKeyJwk, Is.Not.Null.And.Not.Empty, "a Server Share drive has a write-only key pair");
        return (frodo, sam, drive, publicKeyJwk!);
    }
}
