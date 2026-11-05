#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Ported.Transit;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Auth;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

using static Odin.Hosting.Tests.V2.ZeroKnowledge.ZkTestFiles;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// A peer's writes on a Private drive have no inbox (docs/terminology.md, "Files from others"): a deposit's update,
/// delete and reactions are written at once, as on a Private Share drive.
/// </summary>
[TestFixture]
public class PrivateDrivePeerWritesTests : V2Fixture
{
    private const string Thumbsup = ":thumbsup:";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task ASendersDeleteOfTheirDepositLandsAtOnce()
    {
        var (frodo, sam, drive, keys) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, uniqueId, "Sam's", remoteDrive: drive);
        var received = await FrodosAsync(frodo, drive, uniqueId);

        var delete = await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, received.FileMetadata.GlobalTransitId!.Value);
        Assert.That(delete.StatusCode, Is.EqualTo(HttpStatusCode.OK), delete.Error?.Content);
        await sam.Sync.DrainOutboxAsync();

        var after = await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, received.FileId);
        Assert.That(after.Content?.FileState, Is.EqualTo(FileState.Deleted), "deleted with no inbox to drain");
    }

    [Test]
    public async Task ASendersDeleteIsRefusedOnceTheOwnerChangedTheDeposit()
    {
        // As a change naming no tag (docs/file-access.md, "Changing a file you sent"): Frodo took the deposit over
        var (frodo, sam, drive, keys) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var (fileKey, _) = await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, uniqueId, "Sam's", remoteDrive: drive);
        var received = await FrodosAsync(frodo, drive, uniqueId);
        var newKey = WithNewIv(fileKey);
        var takeOver = await UpdateAsync(frodo, received.FileId, uniqueId, newKey, ZkBrowser.LockFileKey(newKey, keys.DriveKey),
            received.FileMetadata.VersionTag, drive: drive);
        Assert.That(takeOver.StatusCode, Is.EqualTo(HttpStatusCode.OK), takeOver.Error?.Content);

        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, received.FileMetadata.GlobalTransitId!.Value);
        await sam.Sync.DrainOutboxAsync();

        var after = await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, received.FileId);
        Assert.That(after.Content?.FileState, Is.EqualTo(FileState.Active), "refused: Frodo changed it since");

        // The same delete of a deposit no one else changed lands, so the refusal above was the sender rule's
        var otherId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, otherId, "Sam's other", remoteDrive: drive);
        var other = await FrodosAsync(frodo, drive, otherId);
        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, other.FileMetadata.GlobalTransitId!.Value);
        await sam.Sync.DrainOutboxAsync();
        Assert.That((await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, other.FileId)).Content?.FileState,
            Is.EqualTo(FileState.Deleted), "the same route, unchanged since: deleted");
    }

    [Test]
    public async Task APeersReactionOnADepositLandsAtOnce()
    {
        var (frodo, sam, drive, keys) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, uniqueId, "Sam's", remoteDrive: drive);
        var received = await FrodosAsync(frodo, drive, uniqueId);
        var file = new GlobalTransitIdFileIdentifier { GlobalTransitId = received.FileMetadata.GlobalTransitId!.Value, TargetDrive = drive };

        await PeerReactions.AddReactionAsync(sam, frodo.Identity, file, Thumbsup);

        var after = await FrodosAsync(frodo, drive, uniqueId);
        Assert.That(after.FileMetadata.ReactionPreview?.Reactions.Values.Select(r => (r.ReactionContent, r.Count)),
            Is.EqualTo(new[] { (Thumbsup, 1) }), "applied with no inbox to drain");
    }

    [Test]
    public async Task APrivateDriveTakesNoCommentFromAPeer()
    {
        // Only owner callers comment on a Private drive (docs/terminology.md), even a peer who may read the file and holds
        // drive Comment: Frodo's unencrypted file, open to connections, which Sam may read
        var (frodo, sam, drive, _) = await ArrangeAsync(DrivePermission.Read | DrivePermission.Write | DrivePermission.Comment);
        var shared = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        shared.AppData.UniqueId = Guid.NewGuid();
        var upload = await frodo.V1.Drive.UploadNewMetadata(drive, shared);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var received = await FrodosAsync(frodo, drive, shared.AppData.UniqueId.Value);

        var metadata = SampleMetadataData.CreateWithContent(fileType: 101, "Sam's comment", AccessControlList.Connected);
        metadata.AllowDistribution = true;
        metadata.ReferencedFile = new GlobalTransitIdFileIdentifier { GlobalTransitId = received.FileMetadata.GlobalTransitId!.Value, TargetDrive = drive };
        var writer = new DriveWriterV2Client(sam.Identity, sam.Factory, FileSystemType.Comment);
        var comment = await writer.CreateNewUnencryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
            new UploadManifest { PayloadDescriptors = [] }, [],
            transitOptions: new TransitOptions { IsTransient = true, Recipients = [frodo.Identity.DomainName], RemoteTargetDrive = drive });
        Assert.That(comment.StatusCode, Is.EqualTo(HttpStatusCode.OK), comment.Error?.Content);
        await sam.Sync.DrainOutboxAsync();

        var comments = await frodo.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10 }
        }, FileSystemType.Comment);
        Assert.That(comments.StatusCode, Is.EqualTo(HttpStatusCode.OK), comments.Error?.Content);
        Assert.That(comments.Content!.SearchResults, Is.Empty, "refused");
    }

    /// <summary>Frodo's Private drive, on which Sam holds <paramref name="samsPermission"/> (write, react and comment by default), and its keys.</summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam, TargetDrive Drive, ZkBrowser.DriveKeys Keys)> ArrangeAsync(
        DrivePermission samsPermission = DrivePermission.Write | DrivePermission.React | DrivePermission.Comment)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
        var drive = TargetDrive.NewTargetDrive();
        var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);
        await frodo.Admin.CreateDrive(drive, "Frodo's private", allowAnonymousReads: false, sharingModel: DriveSharingModel.Private,
            zkKeys: keys.Request);
        await PeerFlow.ConnectAsync(sam, frodo, drive, samsPermission);
        return (frodo, sam, drive, keys);
    }

    private static async Task<SharedSecretEncryptedFileHeader> FrodosAsync(OwnerSession frodo, TargetDrive drive, Guid uniqueId)
    {
        var read = await frodo.Drives.Reader.GetFileHeaderByUniqueIdAsync(uniqueId, drive.Alias);
        Assert.That(read.StatusCode, Is.EqualTo(HttpStatusCode.OK), read.Error?.Content);
        return read.Content!;
    }
}
