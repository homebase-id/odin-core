using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Outgoing.Drive.Reactions;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of
/// tests/apps/Odin.Hosting.Tests/OwnerApi/Transit/ReactionContent/TransitReactionContentOwnerTestsAuthenticatedReactions.cs
///
/// Sam follows Pippin, so Pippin's channel post lands on Sam's feed drive. Sam — merely authenticated,
/// not connected — can then react to that post over transit and delete the reaction again, and each
/// change shows up in the reaction preview on his own feed copy.
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     Every <c>WaitForEmptyOutbox</c> became <see cref="PeerFlow.DistributeAsync(OwnerSession, OwnerSession, TargetDrive)"/>:
///     <c>Sync.DrainOutboxAsync</c> (a passive poll needing the outbox background service, which this
///     host never starts) <em>plus</em> a <c>Sync.ProcessInboxAsync(FeedDrive)</c> on Sam. The V1
///     original relied on the recipient's background inbox service to move the feed item the rest of
///     the way; here that step is explicit.
///   </description></item>
///   <item><description>
///     Transit reaction calls go through <see cref="PeerReactions"/> — see that class for why the V1
///     Refit interface rather than a <c>_Universal</c> client.
///   </description></item>
///   <item><description>
///     <c>GetFollower</c> returns an <c>ApiResponse</c> here rather than the bare definition, so the
///     original's "is not null" check on the definition is now a status check plus a content check.
///   </description></item>
/// </list>
/// </remarks>
[TestFixture]
public class TransitReactionContentOwnerTestsAuthenticatedReactions : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Pippin, Identities.Sam];

    [Test]
    public async Task WhenOwnerFollows_AnonymousDrive_OwnerCanSendReactionAndDelete_FromFeed()
    {
        //Scenario: when sam follows Pippin, content shows in Sam's feed from Pippin
        //Sam can react and delete the reaction because he follows and can send over transit

        //Note: not sure if that's a good thing BUT technically sam is authenticated

        const string reactionContent = ":r:";

        var pippin = await LoginAsOwner(Identities.Pippin);
        var pippinChannelDrive = new TargetDrive
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await pippin.Admin.CreateDrive(pippinChannelDrive, "A Channel Drive", allowAnonymousReads: true, ownerOnly: false,
            allowSubscriptions: true);

        var sam = await LoginAsOwner(Identities.Sam);
        await sam.V1.Follower.FollowIdentity(pippin.Identity, FollowerNotificationType.AllNotifications, null);

        //
        // Validate Pippin knows Sam follows him
        //
        var samFollowingPippinDefinition = await pippin.V1.Follower.GetFollower(sam.Identity);
        Assert.That(samFollowingPippinDefinition.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(samFollowingPippinDefinition.Content, Is.Not.Null);

        //
        // Pippin uploads a post
        //
        var uploadedContent = "I'm Hungry!";
        var uploadResult = await UploadUnencryptedContentToChannelAsync(pippin, pippinChannelDrive, uploadedContent,
            acl: AccessControlList.Anonymous);

        await PeerFlow.DistributeAsync(pippin, sam, WellKnownAppDrives.FeedDrive);

        //
        // Get the post from Sam's feed drive, validate we got it
        //
        var headerOnSamsFeed = await GetHeaderFromFeedDriveAsync(sam, uploadResult);
        Assert.That(headerOnSamsFeed.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));

        //
        // Sam adds reaction from Sam's feed to Pippin's channel
        //
        await PeerReactions.AddReactionAsync(sam, pippin.Identity,
            uploadResult.GlobalTransitIdFileIdentifier,
            reactionContent);

        await PeerFlow.DistributeAsync(pippin, sam, WellKnownAppDrives.FeedDrive);

        //
        // Sam queries across Transit to get all reactions
        //
        var response = await PeerReactions.GetAllReactionsAsync(sam, pippin.Identity, new GetRemoteReactionsRequest
        {
            File = uploadResult.GlobalTransitIdFileIdentifier,
            Cursor = "",
            MaxRecords = 100
        });

        Assert.That(response.Reactions.Count, Is.EqualTo(1));
        var theReaction = response.Reactions.SingleOrDefault();
        Assert.That(theReaction, Is.Not.Null);
        Assert.That(theReaction!.ReactionContent, Is.EqualTo(reactionContent));
        Assert.That(theReaction.GlobalTransitIdFileIdentifier, Is.EqualTo(uploadResult.GlobalTransitIdFileIdentifier));

        //
        // Get the post from Sam's feed drive, validate we got it
        //
        var headerOnSamsFeedWithReaction = await GetHeaderFromFeedDriveAsync(sam, uploadResult);
        Assert.That(headerOnSamsFeedWithReaction.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        var reactionSummaryValue =
            headerOnSamsFeedWithReaction.FileMetadata.ReactionPreview.Reactions.Values.SingleOrDefault(r => r.ReactionContent == reactionContent);
        Assert.That(reactionSummaryValue, Is.Not.Null, "could not find reaction on Sam's feed");

        // Now, Sam deletes the reactions
        var deleteReactionResponse =
            await PeerReactions.DeleteReactionAsync(sam, pippin.Identity, reactionContent, uploadResult.GlobalTransitIdFileIdentifier);
        // The original asserted IsSuccessStatusCode here; the endpoint answers 204.
        Assert.That(deleteReactionResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        await PeerFlow.DistributeAsync(pippin, sam, WellKnownAppDrives.FeedDrive);

        //
        // Get the post from sam's feed drive again, it should have the header updated
        //
        var headerOnSamsFeedWithAfterReactionWasDeleted = await GetHeaderFromFeedDriveAsync(sam, uploadResult);
        Assert.That(headerOnSamsFeedWithAfterReactionWasDeleted.FileMetadata.ReactionPreview.Reactions, Is.Empty);
    }

    // ---------------------------------------------------------------------------------------------

    private static async Task<UploadResult> UploadUnencryptedContentToChannelAsync(
        OwnerSession owner,
        TargetDrive targetDrive,
        string uploadedContent,
        bool allowDistribution = true,
        AccessControlList acl = null)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = allowDistribution,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = acl ?? AccessControlList.Connected
        };

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata, FileSystemType.Standard);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content;
    }

    /// <summary>The post as it landed on <paramref name="owner"/>'s feed drive.</summary>
    private static async Task<SharedSecretEncryptedFileHeader> GetHeaderFromFeedDriveAsync(
        OwnerSession owner, UploadResult uploadResult)
    {
        var header = await TransitScenario.SingleByGlobalTransitIdAsync(owner, new GlobalTransitIdFileIdentifier
        {
            TargetDrive = WellKnownAppDrives.FeedDrive,
            GlobalTransitId = uploadResult.GlobalTransitId.GetValueOrDefault()
        });

        Assert.That(header.FileState, Is.EqualTo(FileState.Active));
        Assert.That(header.FileMetadata.GlobalTransitId, Is.EqualTo(uploadResult.GlobalTransitId));

        return header;
    }
}
