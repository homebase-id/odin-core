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
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A Server Share drive takes a peer's sealed file at once, without the inbox, and its server makes it its own file on
/// the first read whose request carries the drive key (docs/server-share-sealed-arrivals-plan.md, step 1).
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
        var sharedSecret = frodo.SharedSecret;
        var keyHeader = read.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
        Assert.That(keyHeader.Decrypt(read.FileMetadata.AppData.Content.FromBase64()).ToStringFromUtf8Bytes(), Is.EqualTo(Content),
            "the owner gets it as any file, locked with their shared secret");

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
    public async Task ADepositSealedToAnotherKeyIsRefused()
    {
        var (frodo, sam, drive, _) = await ArrangeAsync();

        await SendDepositAsync(sam, sam, frodo, ZkBrowser.MakeKeyPair().PublicKeyJwk, Guid.NewGuid(), Content, remoteDrive: drive);

        Assert.That(await StoredKeyHeadersAsync(frodo, drive), Is.Empty, "the server could never open it");
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
    public async Task ACommunityChannelTakesNoDeposit()
    {
        // It re-locks a member's post and sends it on as it lands, which needs the file's key then (FeedDriveDistributionRouter)
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync(communityChannel: true);

        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, Guid.NewGuid(), Content, remoteDrive: drive);

        Assert.That(await StoredKeyHeadersAsync(frodo, drive), Is.Empty, "refused on arrival");
        Assert.That(await InboxCountAsync(frodo, drive), Is.EqualTo(0));
    }

    [Test]
    public async Task ASendersSealedChangeAfterTheConversionIsRefusedButTheirDeleteLands()
    {
        // Step 1 has one rule for every drive: a sealed key header replaces only a sealed one (ZkDriveFiles.AssertMayReplace)
        var (frodo, sam, drive, publicKeyJwk) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, Content, remoteDrive: drive);
        var read = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);

        await SendDepositAsync(sam, sam, frodo, publicKeyJwk, uniqueId, "Sam's, changed", remoteDrive: drive,
            versionTag: read.FileMetadata.VersionTag);
        var after = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(after.FileMetadata.VersionTag, Is.EqualTo(read.FileMetadata.VersionTag), "refused: the server's own key header now");

        // The conversion kept the version tag, so the sender's record of their last write still matches
        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, read.FileMetadata.GlobalTransitId!.Value);
        await sam.Sync.DrainOutboxAsync();
        await frodo.Sync.ProcessInboxAsync(drive);
        var deleted = await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, read.FileId);
        Assert.That(deleted.Content?.FileState, Is.EqualTo(Odin.Services.Drives.DriveCore.Storage.FileState.Deleted));
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
        var drive = TargetDrive.NewTargetDrive();
        var create = await frodo.Admin.CreateDrive(drive, "Frodo's shared", allowAnonymousReads: false,
            allowSubscriptions: communityChannel,
            attributes: communityChannel ? new Dictionary<string, string> { [BuiltInDriveAttributes.IsCollaborativeChannel] = "true" } : null);
        Assert.That(create.StatusCode, Is.EqualTo(HttpStatusCode.OK), create.Error?.Content);
        await PeerFlow.ConnectAsync(sam, frodo, drive, DrivePermission.Write);
        var publicKeyJwk = (await frodo.Admin.GetDrive(drive)).WriteOnlyPublicKeyJwk;
        Assert.That(publicKeyJwk, Is.Not.Null.And.Not.Empty, "a Server Share drive has a write-only key pair");
        return (frodo, sam, drive, publicKeyJwk!);
    }
}
