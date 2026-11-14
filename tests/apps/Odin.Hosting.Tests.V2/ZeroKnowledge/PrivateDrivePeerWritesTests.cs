#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Base;
using Odin.Core;
using Autofac;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
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
using Odin.Services.Drives.Reactions.Redux.Group;

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
        var received = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);

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
        var received = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
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
        var other = await HeaderByUniqueIdAsync(frodo, otherId, drive);
        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, other.FileMetadata.GlobalTransitId!.Value);
        await sam.Sync.DrainOutboxAsync();
        Assert.That((await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, other.FileId)).Content?.FileState,
            Is.EqualTo(FileState.Deleted), "the same route, unchanged since: deleted");
    }

    [Test]
    public async Task ASendersDeleteNamingTheVersionTheOwnerGaveTheDepositLands()
    {
        // A sender who names the version the file has now has seen the owner's change: as their update naming it
        var (frodo, sam, drive, keys) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        var (fileKey, _) = await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, uniqueId, "Sam's", remoteDrive: drive);
        var received = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        var newKey = WithNewIv(fileKey);
        var takeOver = await UpdateAsync(frodo, received.FileId, uniqueId, newKey, ZkBrowser.LockFileKey(newKey, keys.DriveKey),
            received.FileMetadata.VersionTag, drive: drive);
        Assert.That(takeOver.StatusCode, Is.EqualTo(HttpStatusCode.OK), takeOver.Error?.Content);
        var gtid = received.FileMetadata.GlobalTransitId!.Value;

        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, gtid, received.FileMetadata.VersionTag);
        await sam.Sync.DrainOutboxAsync();
        var changed = await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, received.FileId);
        Assert.That(changed.Content?.FileState, Is.EqualTo(FileState.Active), "naming the version before Frodo's change: refused");

        await sam.Drives.PeerWriter.SendDeleteRequestOverPeer(frodo.Identity, drive, gtid, changed.Content!.FileMetadata.VersionTag);
        await sam.Sync.DrainOutboxAsync();
        Assert.That((await frodo.Drives.Reader.GetFileHeaderAsync(drive.Alias, received.FileId)).Content?.FileState,
            Is.EqualTo(FileState.Deleted), "naming the version Frodo's change gave it: deleted");
    }

    [Test]
    public async Task APeersReactionOnADepositLandsAtOnce()
    {
        // The direct peer reaction route (PeerIncomingReactionService), which never had an inbox; the group route is
        // APeersGroupReactionWithOnlyReactLandsAtOnce
        var (frodo, sam, drive, keys) = await ArrangeAsync();
        var uniqueId = Guid.NewGuid();
        await SendDepositAsync(sam, sam, frodo, keys.WriteOnlyPublicKeyJwk, uniqueId, "Sam's", remoteDrive: drive);
        var received = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        var file = new GlobalTransitIdFileIdentifier { GlobalTransitId = received.FileMetadata.GlobalTransitId!.Value, TargetDrive = drive };

        await PeerReactions.AddReactionAsync(sam, frodo.Identity, file, Thumbsup);

        var after = await HeaderByUniqueIdAsync(frodo, uniqueId, drive);
        Assert.That(after.FileMetadata.ReactionPreview?.Reactions.Values.Select(r => (r.ReactionContent, r.Count)),
            Is.EqualTo(new[] { (Thumbsup, 1) }), "applied with no inbox to drain");
    }

    [Test]
    public async Task APeersGroupReactionWithOnlyReactLandsAtOnce()
    {
        // The group reaction route (PeerIncomingGroupReactionInboxRouterService) reacts to the sender's own copy and sends
        // the reaction on, so Sam needs a copy with the file's global transit id.  A deposit leaves him none (it is sent
        // from his transient drive) and a zero-knowledge drive sends nothing, so Sam sends an unencrypted file from his
        // Server Share drive to Frodo's Private one.  Then Frodo narrows Sam's grant to React alone, and Sam's reaction
        // on his copy is applied on Frodo's at once
        var (frodo, zkMasterKey) = await ZeroKnowledgeFrodoAsync();
        var sam = await LoginAsOwner(Identities.Sam);
        var (drive, _) = await ZkOwnerSteps.CreateZeroKnowledgeDriveAsync(frodo, zkMasterKey, DriveSharingModel.Private, name: "Frodo's private");
        await sam.Admin.EnsureDrive(drive, "Sam's copies", allowAnonymousReads: false);
        var circleId = Guid.NewGuid();
        await frodo.Admin.CreateCircle(circleId, "Sam's grant", TestUtils.CreatePermissionGrantRequest(drive, DrivePermission.Write));
        Assert.That((await sam.Connections.SendConnectionRequest(frodo.Identity)).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await frodo.Connections.AcceptConnectionRequest(sam.Identity, [(GuidId)circleId])).StatusCode,
            Is.EqualTo(HttpStatusCode.OK));

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected, allowDistribution: true);
        metadata.AppData.UniqueId = Guid.NewGuid();
        var upload = await sam.V1.Drive.UploadNewMetadata(drive, metadata, new TransitOptions { Recipients = [frodo.Identity.DomainName] });
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        await sam.Sync.DrainOutboxAsync();
        var before = await HeaderByUniqueIdAsync(frodo, metadata.AppData.UniqueId.Value, drive);
        Assert.That(before.FileMetadata.GlobalTransitId, Is.EqualTo(upload.Content!.GlobalTransitId), "Sam's file, on Frodo's drive");

        var definition = await frodo.Admin.GetCircleDefinition(circleId);
        definition.DriveGrants.Single().PermissionedDrive.Permission = DrivePermission.React;
        var narrow = await frodo.Admin.TryUpdateCircleDefinition(definition);
        Assert.That(narrow.StatusCode, Is.EqualTo(HttpStatusCode.OK), narrow.Error?.Content);
        // A change of a circle's grants doesn't reset Frodo's cached peer contexts today (file-access.md, "Peers' contexts
        // are cached"); the hour they are cached for would
        await Host.GetTenantScope(frodo.Identity.DomainName).Resolve<OdinContextCache>().ResetAsync();

        var reaction = await sam.V1.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = upload.Content.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = Thumbsup,
            TransitOptions = new ReactionTransitOptions { Recipients = [frodo.Identity] }
        });
        Assert.That(reaction.StatusCode, Is.EqualTo(HttpStatusCode.OK), reaction.Error?.Content);
        await sam.Sync.DrainOutboxAsync();

        var after = await HeaderByUniqueIdAsync(frodo, metadata.AppData.UniqueId.Value, drive);
        Assert.That(after.FileMetadata.ReactionPreview?.Reactions.Values.Select(r => (r.ReactionContent, r.Count)),
            Is.EqualTo(new[] { (Thumbsup, 1) }), "applied with no inbox to drain, by React alone");
    }

    [TestCase(DriveSharingModel.Private, 0, TestName = "APrivateDriveTakesNoCommentFromAPeer")]
    [TestCase(DriveSharingModel.Collaborative, 1, TestName = "AServerShareDriveTakesTheSameCommentFromAPeer")]
    public async Task OnlyOwnerCallersCommentOnAPrivateDrive(DriveSharingModel model, int expectedComments)
    {
        // Only owner callers comment on a Private drive (docs/terminology.md), even a peer who may read the file and holds
        // drive Comment: Frodo's unencrypted file, open to connections, which Sam may read.  The same comment on a Server
        // Share drive lands, so the refusal is the rule's, not a broken transfer
        var (frodo, sam, drive, _) = await ArrangeOnAsync(model, DrivePermission.Read | DrivePermission.Write | DrivePermission.Comment);
        var shared = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        shared.AppData.UniqueId = Guid.NewGuid();
        var upload = await frodo.V1.Drive.UploadNewMetadata(drive, shared);
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), upload.Error?.Content);
        var received = await HeaderByUniqueIdAsync(frodo, shared.AppData.UniqueId.Value, drive);

        var metadata = SampleMetadataData.CreateWithContent(fileType: 101, "Sam's comment", AccessControlList.Connected);
        metadata.AllowDistribution = true;
        metadata.ReferencedFile = new GlobalTransitIdFileIdentifier { GlobalTransitId = received.FileMetadata.GlobalTransitId!.Value, TargetDrive = drive };
        var writer = new DriveWriterV2Client(sam.Identity, sam.Factory, FileSystemType.Comment);
        var comment = await writer.CreateNewUnencryptedFile(SystemDriveConstants.TransientTempDrive.Alias, metadata,
            new UploadManifest { PayloadDescriptors = [] }, [],
            transitOptions: new TransitOptions { IsTransient = true, Recipients = [frodo.Identity.DomainName], RemoteTargetDrive = drive });
        Assert.That(comment.StatusCode, Is.EqualTo(HttpStatusCode.OK), comment.Error?.Content);
        await PeerFlow.DistributeAsync(sam, frodo, drive);

        var comments = await frodo.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { TargetDrive = drive },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 10 }
        }, FileSystemType.Comment);
        Assert.That(comments.StatusCode, Is.EqualTo(HttpStatusCode.OK), comments.Error?.Content);
        Assert.That(comments.Content!.SearchResults.Select(c => c.FileMetadata.SenderOdinId), Is.EqualTo(Enumerable.Repeat(Identities.Sam, expectedComments)),
            model == DriveSharingModel.Private ? "refused" : "landed");
    }

    /// <summary>Frodo's Private drive, on which Sam holds <paramref name="samsPermission"/> (write, react and comment by default), and its keys.</summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam, TargetDrive Drive, ZkBrowser.DriveKeys Keys)> ArrangeAsync(
        DrivePermission samsPermission = DrivePermission.Write | DrivePermission.React | DrivePermission.Comment)
    {
        var (frodo, sam, drive, keys) = await ArrangeOnAsync(DriveSharingModel.Private, samsPermission);
        return (frodo, sam, drive, keys!);
    }

    /// <summary>
    /// Frodo's drive of <paramref name="model"/>, on which Sam holds <paramref name="samsPermission"/>, and its keys when it is
    /// zero knowledge.
    /// </summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam, TargetDrive Drive, ZkBrowser.DriveKeys? Keys)> ArrangeOnAsync(
        DriveSharingModel model, DrivePermission samsPermission)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        TargetDrive drive;
        ZkBrowser.DriveKeys? keys = null;
        if (model.IsZeroKnowledgeDrive())
        {
            var (zkMasterKey, _, _) = await ZkOwnerSteps.MakeZeroKnowledgeAsync(frodo, OwnerLogin.DefaultPassword);
            (drive, keys) = await ZkOwnerSteps.CreateZeroKnowledgeDriveAsync(frodo, zkMasterKey, model, name: "Frodo's private");
        }
        else
        {
            drive = TargetDrive.NewTargetDrive();
            await frodo.Admin.CreateDrive(drive, "Frodo's shared", allowAnonymousReads: false, sharingModel: model);
        }

        await PeerFlow.ConnectAsync(sam, frodo, drive, samsPermission);
        return (frodo, sam, drive, keys);
    }
}
