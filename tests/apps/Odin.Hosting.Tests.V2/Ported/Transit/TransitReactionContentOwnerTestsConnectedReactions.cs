using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Incoming.Reactions;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of
/// tests/apps/Odin.Hosting.Tests/OwnerApi/Transit/ReactionContent/TransitReactionContentOwnerTestsConnectedReactions.cs
///
/// Sam is connected to Pippin and holds read/write on Pippin's channel drive through a circle. He can
/// then add, list and delete reactions on Pippin's channel post over transit.
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     Transit reaction calls go through <see cref="PeerReactions"/>; the drive and circle are created
///     through <c>owner.Admin</c>, and the connection handshake through <c>owner.Connections</c>. The
///     flow is hand-rolled rather than run through <c>PeerFlow</c>: only Pippin holds the channel
///     drive, and Sam grants nothing back.
///   </description></item>
///   <item><description>
///     The trailing disconnect / unfollow / disconnect calls asserted nothing and are dropped —
///     per-test reset restores that state.
///   </description></item>
///   <item><description>
///     The three <c>[Test, Explicit("TODO")]</c> stubs are carried verbatim, including their
///     <c>Assert.Inconclusive</c> bodies. Being <c>[Explicit]</c>, they do not run under
///     <c>dotnet test</c>.
///   </description></item>
/// </list>
/// Carried oddity, behaviour left as found: both real tests have Sam follow Pippin and create the
/// circle, but nothing reads the follow — the reaction path here is the connected one, and the
/// original had already commented out its own "validate Pippin knows Sam follows him" assertion. The
/// follow is kept because removing it would change the arrange under test. Two private helpers on the
/// original (<c>UploadComment</c>, and the <c>allowDistribution</c> / <c>acl</c> parameters of
/// <c>UploadUnencryptedContentToChannel</c>) had no caller and are not carried.
/// </remarks>
[TestFixture]
public class TransitReactionContentOwnerTestsConnectedReactions : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Pippin, Identities.Sam];

    [Test]
    public async Task ConnectedIdentity_CanSendAndGetAllReactions_OverTransit_ForPublicChannel_WithNoCircles()
    {
        const string reactionContent = ":k:";

        var (pippin, sam, uploadResult) = await PrepareScenarioAsync();

        var theReaction = await AddAndReadBackTheReactionAsync(sam, pippin, uploadResult, reactionContent);
        Assert.That(theReaction.GlobalTransitIdFileIdentifier, Is.EqualTo(uploadResult.GlobalTransitIdFileIdentifier));
    }

    [Test]
    public async Task ConnectedIdentity_CanSendAndDeleteReactionsOverTransit_ForPublicChannel_WithNoCircles()
    {
        const string reactionContent = ":k:";

        var (pippin, sam, uploadResult) = await PrepareScenarioAsync();

        await AddAndReadBackTheReactionAsync(sam, pippin, uploadResult, reactionContent);

        // now delete it
        await PeerReactions.DeleteReactionAsync(sam, pippin.Identity, reactionContent, uploadResult.GlobalTransitIdFileIdentifier);

        var shouldBeDeletedResponse = await PeerReactions.GetAllReactionsAsync(sam, pippin.Identity,
            uploadResult.GlobalTransitIdFileIdentifier);

        Assert.That(shouldBeDeletedResponse.Reactions, Is.Empty);
    }

    [Test, Explicit("TODO")]
    public Task ConnectedIdentity_CanSendAndDeleteAllReactionsOnFile_ReactionsOverTransit_ForPublicChannel_WithNoCircles()
    {
        Assert.Inconclusive("TODO DeleteAllReactionsOnFile");
        return Task.CompletedTask;
    }

    [Test, Explicit("TODO")]
    public Task ConnectedIdentity_CanSendAndGetReactionCountsByFile_ReactionsOverTransit_ForPublicChannel_WithNoCircles()
    {
        Assert.Inconclusive("TODO GetReactionCountsByFile");
        return Task.CompletedTask;
    }

    [Test, Explicit("TODO")]
    public Task ConnectedIdentity_CanSendAnd_GetReactionsByIdentity_ReactionsOverTransit_ForPublicChannel_WithNoCircles()
    {
        Assert.Inconclusive("TODO GetReactionsByIdentity");
        return Task.CompletedTask;
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Sam reacts to Pippin's post over transit and reads the reaction back: there is exactly one, and
    /// it carries <paramref name="reactionContent"/>. Returns it, for the caller that asserts further.
    /// </summary>
    private static async Task<PerimeterReaction> AddAndReadBackTheReactionAsync(
        OwnerSession sam, OwnerSession pippin, UploadResult uploadResult, string reactionContent)
    {
        //
        // Sam adds reaction from Sam's feed to Pippin's channel
        //
        await PeerReactions.AddReactionAsync(sam, pippin.Identity,
            uploadResult.GlobalTransitIdFileIdentifier,
            reactionContent);

        var response = await PeerReactions.GetAllReactionsAsync(sam, pippin.Identity,
            uploadResult.GlobalTransitIdFileIdentifier);

        Assert.That(response.Reactions.Count, Is.EqualTo(1));
        var theReaction = response.Reactions.SingleOrDefault();
        Assert.That(theReaction, Is.Not.Null);
        Assert.That(theReaction!.ReactionContent, Is.EqualTo(reactionContent));

        return theReaction;
    }

    /// <summary>
    /// The arrange both real tests share: Pippin's channel drive, Sam following and connected with
    /// read/write on it through a circle, and one post of Pippin's to react to.
    /// </summary>
    private async Task<(OwnerSession Pippin, OwnerSession Sam, UploadResult UploadResult)> PrepareScenarioAsync()
    {
        var pippin = await LoginAsOwner(Identities.Pippin);
        var pippinChannelDrive = new TargetDrive
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await pippin.Admin.CreateDrive(pippinChannelDrive, "A Channel Drive", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        var sam = await LoginAsOwner(Identities.Sam);
        await sam.V1.Follower.FollowIdentity(pippin.Identity, FollowerNotificationType.AllNotifications, null);

        var targetCircleId = Guid.NewGuid();
        await pippin.Admin.CreateCircle(targetCircleId, "Garden channel circle",
            TestUtils.CreatePermissionGrantRequest(pippinChannelDrive, DrivePermission.ReadWrite));

        var sendRequest = await sam.Connections.SendConnectionRequest(pippin.Identity, new List<GuidId>());
        Assert.That(sendRequest.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var accept = await pippin.Connections.AcceptConnectionRequest(sam.Identity, new List<GuidId> { targetCircleId });
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // Pippin uploads a post
        //
        var uploadedContent = "I'm Hungry!";
        var uploadResult = await UploadUnencryptedContentToChannelAsync(pippin, pippinChannelDrive, uploadedContent);

        return (pippin, sam, uploadResult);
    }

    private static async Task<UploadResult> UploadUnencryptedContentToChannelAsync(
        OwnerSession owner, TargetDrive targetDrive, string uploadedContent)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata, FileSystemType.Standard);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return response.Content;
    }
}
