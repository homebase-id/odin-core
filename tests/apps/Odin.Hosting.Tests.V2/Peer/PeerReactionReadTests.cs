#nullable enable
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Peer;

/// <summary>
/// Covers the peer reaction READ routes added for
/// <see href="https://github.com/homebase-id/odin-core/issues/1610">#1610</see>. Before them a
/// client could only read its own host's copy of another identity's post, which holds nothing but
/// the reactions it sent itself — so "who reacted" on a followed post showed only you, while the
/// header's reaction count (correct) disagreed.
///
/// Setup is Sam-hosts / Frodo-reads: Sam owns the drive and the file, Sam reacts on it, and Frodo —
/// connected with <see cref="DrivePermission.Read"/> — reads those reactions over peer. The
/// reaction Frodo gets back is one he did not author and has no local copy of, which is exactly the
/// case that was unreachable.
///
/// Reaction WRITES already go over peer via the outbox and are not re-tested here.
/// </summary>
[TestFixture]
public class PeerReactionReadTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task GetReactions_OverPeer_ReturnsReactionsAuthoredByTheHost()
    {
        var (frodo, sam, drive, gtid) = await SetupSamPostWithReactionAsync(":like:");

        var response = await frodo.Drives.Peer.GetReactionsByGtidAsync(sam.Identity, drive.Alias, gtid);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"peer reaction list failed: {response.StatusCode}");
        var reactions = response.Content!.Reactions;
        Assert.That(reactions.Count, Is.EqualTo(1));
        Assert.That(reactions[0].ReactionContent, Is.EqualTo(":like:"));
        Assert.That(reactions[0].OdinId, Is.EqualTo(sam.Identity.DomainName),
            "the reaction Frodo reads over peer is Sam's, not his own");
    }

    /// <summary>
    /// The response deliberately uses the local <c>GetReactionsResponse</c> shape rather than the
    /// perimeter's <c>GetReactionsPerimeterResponse</c>, so a client can decode a peer file's
    /// reactions with the decoder it already uses locally. FileId is meaningless across identities
    /// and comes back zeroed.
    /// </summary>
    [Test]
    public async Task GetReactions_OverPeer_ReturnsTheLocalResponseShape_WithZeroedFileId()
    {
        var (frodo, sam, drive, gtid) = await SetupSamPostWithReactionAsync(":wave:");

        var peerResponse = await frodo.Drives.Peer.GetReactionsByGtidAsync(sam.Identity, drive.Alias, gtid);
        Assert.That(peerResponse.IsSuccessStatusCode, Is.True);

        var peerReaction = peerResponse.Content!.Reactions.Single();
        Assert.That(peerReaction.FileId.FileId, Is.EqualTo(default(System.Guid)));
        Assert.That(peerReaction.FileId.DriveId, Is.EqualTo(default(System.Guid)));

        // Same values Sam's own local read produces -- that is the point of the shape choice.
        var localResponse = await sam.Drives.Reactions.GetAllReactionsAsync(drive.Alias, (await FileIdOf(sam, drive, gtid)));
        Assert.That(localResponse.IsSuccessStatusCode, Is.True);
        var localReaction = localResponse.Content!.Reactions.Single();
        Assert.That(peerReaction.ReactionContent, Is.EqualTo(localReaction.ReactionContent));
        Assert.That(peerReaction.OdinId, Is.EqualTo(localReaction.OdinId));

        // Created is deliberately not asserted to be a real timestamp: it is 0 here, and equally 0
        // on the local read above, because the DriveReactions table has no created column
        // (DriveReactionsRecord: rowId/identityId/driveId/postId/identity/singleReaction) and
        // DriveQuery.GetReactionsByFileAsync never populates Reaction.Created. That gap predates
        // these peer routes and affects every reaction read path; fixing it needs a schema change.
        Assert.That(peerReaction.Created, Is.EqualTo(localReaction.Created));
    }

    [Test]
    public async Task GetReactionSummary_OverPeer_ReturnsCounts()
    {
        var (frodo, sam, drive, gtid) = await SetupSamPostWithReactionAsync(":like:");

        var response = await frodo.Drives.Peer.GetReactionSummaryByGtidAsync(sam.Identity, drive.Alias, gtid);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"peer reaction summary failed: {response.StatusCode}");
        Assert.That(response.Content!.Total, Is.EqualTo(1));
        var count = response.Content.Reactions.Single();
        Assert.That(count.ReactionContent, Is.EqualTo(":like:"));
        Assert.That(count.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task GetReactionsByIdentity_OverPeer_ReturnsThatIdentitysReactions()
    {
        var (frodo, sam, drive, gtid) = await SetupSamPostWithReactionAsync(":like:");

        var mine = await frodo.Drives.Peer.GetReactionsByIdentityAndGtidAsync(sam.Identity, drive.Alias, gtid, sam.Identity);
        Assert.That(mine.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"peer reaction by-identity failed: {mine.StatusCode}");
        Assert.That(mine.Content, Is.EquivalentTo(new[] { ":like:" }));

        // An identity that reacted to nothing comes back empty rather than erroring.
        var none = await frodo.Drives.Peer.GetReactionsByIdentityAndGtidAsync(sam.Identity, drive.Alias, gtid, frodo.Identity);
        Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(none.Content, Is.Empty);
    }

    [Test]
    public async Task GetReactions_OverPeer_ReflectsMultipleReactions()
    {
        var (frodo, sam, drive, gtid) = await SetupSamPostWithReactionAsync(":like:");
        var fileId = await FileIdOf(sam, drive, gtid);

        var second = await sam.Drives.Reactions.AddReactionAsync(drive.Alias, fileId, ":wave:");
        Assert.That(second.IsSuccessStatusCode, Is.True, $"second AddReaction failed: {second.StatusCode}");

        var list = await frodo.Drives.Peer.GetReactionsByGtidAsync(sam.Identity, drive.Alias, gtid);
        Assert.That(list.Content!.Reactions.Select(r => r.ReactionContent),
            Is.EquivalentTo(new[] { ":like:", ":wave:" }));

        var summary = await frodo.Drives.Peer.GetReactionSummaryByGtidAsync(sam.Identity, drive.Alias, gtid);
        Assert.That(summary.Content!.Total, Is.EqualTo(2));
    }

    /// <summary>
    /// Sam hosts a drive and a post, Frodo is connected with Read on it, and Sam leaves one reaction.
    /// Returns both sessions plus the drive and the post's GlobalTransitId — the only handle a feed
    /// client holds for a followed identity's post.
    /// </summary>
    private async Task<(OwnerSession Frodo, OwnerSession Sam, TargetDrive Drive, System.Guid Gtid)>
        SetupSamPostWithReactionAsync(string reaction)
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // frodo is the "sender" here only in PeerFlow's naming: the circle it builds grants frodo
        // access on sam's drive, which is the direction this test needs.
        var drive = await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "reactions");

        var metadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        var upload = await sam.Drives.Writer.UploadNewMetadata(drive.Alias, metadata);
        Assert.That(upload.IsSuccessStatusCode, Is.True, $"Sam upload failed: {upload.StatusCode}");

        var gtid = upload.Content!.GlobalTransitId;
        Assert.That(gtid, Is.Not.Null, "the peer reaction routes are keyed by GlobalTransitId");

        var added = await sam.Drives.Reactions.AddReactionAsync(drive.Alias, upload.Content.FileId, reaction);
        Assert.That(added.IsSuccessStatusCode, Is.True, $"Sam AddReaction failed: {added.StatusCode}");

        return (frodo, sam, drive, gtid!.Value);
    }

    private static async Task<System.Guid> FileIdOf(OwnerSession owner, TargetDrive drive, System.Guid gtid)
    {
        var query = await owner.Drives.Reader.GetBatchAsync(drive.Alias, new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1 { GlobalTransitId = new[] { gtid } },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest { MaxRecords = 1, IncludeMetadataHeader = true }
        });

        Assert.That(query.IsSuccessStatusCode, Is.True, $"query by gtid failed: {query.StatusCode}");
        return query.Content!.SearchResults.Single().FileId;
    }
}
