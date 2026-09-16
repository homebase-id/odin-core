using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Ttl;

/// <summary>
/// Port of <c>_Universal/DriveTests/DirectDriveFileTtlPeerTests</c>.
///
/// Ttl must cross peer. This is the entire reason it lives on FileMetadata rather than ServerMetadata:
/// PeerFileWriter deserializes FileMetadata whole from the transfer, while it builds a fresh
/// ServerMetadata on receipt. Without this, a group could not expire its members' copies of a message.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> drive endpoints through the in-process host via
/// <see cref="UniversalDriveApiClient"/>, reached through <c>owner.V1.Drive</c>. Peer traffic runs
/// in-process through <see cref="PeerFlow"/>.
///
/// No caller matrix — the original had none; these are plain <c>[Test]</c> methods run as the two
/// owners, so the <c>SetupCallerWithOwner</c> ordering caveat does not apply.
///
/// V1's <c>WaitForEmptyOutbox</c> / <c>ProcessInbox</c> pair becomes
/// <see cref="PeerFlow.DistributeAsync(OwnerSession, OwnerSession, TargetDrive)"/>: the fast host
/// registers the outbox background service but never starts it, so the passive poll would hang and
/// then throw. The original's <c>PrepareScenario</c> — drive on both sides with anonymous reads off,
/// a circle on the recipient granting the sender Write, then the request/accept handshake — is exactly
/// <see cref="PeerFlow.CreatePeerDriveAsync"/>. The trailing <c>DisconnectIdentities</c> calls are
/// dropped; they only restored state, which <see cref="V2Fixture"/>'s per-test reset already
/// guarantees.
/// </remarks>
[TestFixture]
public class FileTtlPeerTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AnAbsoluteTtlSurvivesTheHopToARecipient()
    {
        // The chat-retention shape: resolved to an absolute time at send, so every member's copy dies
        // at the same moment without any retention message being exchanged.
        var ttl = FileTtl.After(TimeSpan.FromDays(90));

        var recipientTtl = await SendWithTtlAndReadBack(ttl, fileType: 7001);

        Assert.That(recipientTtl, Is.EqualTo(ttl), "the recipient's copy must carry the sender's Ttl");
    }

    [Test]
    public async Task APendingTtlCrossesPeerStillPendingSoEachCopyRunsItsOwnClock()
    {
        // The Snapchat shape. It must arrive still negative: if it had been resolved on the sender it
        // would die by the sender's reading habits rather than the recipient's.
        var ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));

        var recipientTtl = await SendWithTtlAndReadBack(ttl, fileType: 7002);

        Assert.That(recipientTtl, Is.EqualTo(ttl), "a pending Ttl must arrive unresolved");
        Assert.That(FileTtl.IsPendingFirstRead(recipientTtl), Is.True);
    }

    /// <summary>
    /// The Snapchat requirement proper: each copy's clock starts on its own reader's first view. The
    /// recipient reading their payload must resolve *their* copy and leave the sender's alone.
    /// </summary>
    [Test]
    public async Task EachCopyResolvesItsPendingTtlIndependentlyOnItsOwnFirstRead()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var ttl = FileTtl.AfterFirstRead(TimeSpan.FromMinutes(20));

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new UploadAppFileMetaData { Content = "burn", FileType = 7003 },
            AccessControlList = AccessControlList.Connected,
            Ttl = ttl
        };

        var payload = SamplePayloadDefinitions.GetPayloadDefinition1();
        var payloads = new List<TestPayloadDefinition> { payload };
        var manifest = new UploadManifest { PayloadDescriptors = payloads.ToPayloadDescriptorList().ToList() };

        var uploadResponse = await sender.V1.Drive.UploadNewFile(targetDrive, fileMetadata, manifest, payloads,
            new TransitOptions { Recipients = [recipient.Identity] });
        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResponse.Content;

        await PeerFlow.DistributeAsync(sender, recipient, targetDrive);

        var recipientCopy = (await recipient.V1.Drive.QueryByGlobalTransitId(uploadResult!.GlobalTransitIdFileIdentifier))
            .Content!.SearchResults.SingleOrDefault();
        Assert.That(recipientCopy, Is.Not.Null);
        Assert.That(FileTtl.IsPendingFirstRead(recipientCopy!.FileMetadata.Ttl), Is.True, "arrives unresolved");

        // the recipient opens it
        var recipientFile = new ExternalFileIdentifier
        {
            FileId = recipientCopy.FileId,
            TargetDrive = targetDrive
        };
        Assert.That((await recipient.V1.Drive.GetPayload(recipientFile, payload.Key)).IsSuccessStatusCode, Is.True);

        var recipientAfter = (await recipient.V1.Drive.GetFileHeader(recipientFile)).Content;
        Assert.That(FileTtl.IsAbsolute(recipientAfter!.FileMetadata.Ttl), Is.True,
            "the recipient's own read must start the recipient's clock");

        // ...and the sender, who has not opened theirs, is untouched
        var senderAfter = (await sender.V1.Drive.GetFileHeader(uploadResult.File)).Content;
        Assert.That(senderAfter!.FileMetadata.Ttl, Is.EqualTo(ttl),
            "the sender's copy must still be pending - one reader must not burn another's copy");
    }

    private async Task<long> SendWithTtlAndReadBack(long ttl, int fileType)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var targetDrive = TargetDrive.NewTargetDrive();
        await PrepareScenario(sender, recipient, targetDrive, DrivePermission.Write);

        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = true,
            AppData = new UploadAppFileMetaData { Content = "expiring", FileType = fileType },
            AccessControlList = AccessControlList.Connected,
            Ttl = ttl
        };

        var (uploadResponse, _) = await sender.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            new StorageOptions { Drive = targetDrive },
            new TransitOptions { Recipients = [recipient.Identity] });

        Assert.That(uploadResponse.IsSuccessStatusCode, Is.True);
        var uploadResult = uploadResponse.Content;

        await PeerFlow.DistributeAsync(sender, recipient, targetDrive);

        // The recipient holds a different FileId, so the copy has to be found by global transit id
        var queryResponse = await recipient.V1.Drive.QueryByGlobalTransitId(uploadResult!.GlobalTransitIdFileIdentifier);
        Assert.That(queryResponse.IsSuccessStatusCode, Is.True);

        var recipientCopy = queryResponse.Content!.SearchResults.SingleOrDefault();
        Assert.That(recipientCopy, Is.Not.Null, "the recipient should have received the file");

        // the sender's own copy is untouched by the hop
        var senderHeader = (await sender.V1.Drive.GetFileHeader(uploadResult.File)).Content;
        Assert.That(senderHeader!.FileMetadata.Ttl, Is.EqualTo(ttl), "the sender's copy must keep its Ttl");

        return recipientCopy!.FileMetadata.Ttl;
    }

    private static Task PrepareScenario(OwnerSession sender, OwnerSession recipient, TargetDrive targetDrive,
        DrivePermission drivePermissions) =>
        PeerFlow.CreatePeerDriveAsync(sender, recipient, drivePermissions,
            label: "Target drive",
            allowAnonymousReads: false,
            drive: targetDrive);
}
