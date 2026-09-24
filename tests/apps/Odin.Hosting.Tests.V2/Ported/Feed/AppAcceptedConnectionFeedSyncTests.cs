using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using System.Linq;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2.Connections;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// What the channel sync delivers when an <b>app</b> accepted the connection request, and what it
/// cannot (#1784).
/// </summary>
/// <remarks>
/// The fixtures that found #1784 only prove no error is logged, and "no exception" is not "files
/// arrived" -- the sync can succeed at doing nothing. This one asserts the files, which is how the
/// remaining limit was found.
/// <para>
/// Sam follows Frodo <i>before</i> connecting, so back-population delivers only Frodo's anonymous post.
/// Frodo's friends-only post needs the circle he grants when he asks to connect, so it can only arrive
/// via the accept-time sync. Sam's app accepts.
/// </para>
/// <para>
/// The sync now runs -- before the fix it threw on <c>ManageFeed</c> and was swallowed -- and it
/// authenticates to Frodo and pulls his channel list. It still cannot write the encrypted post: sealing
/// into the feed drive needs that drive's storage key, and an app-accepted connection has no way to
/// produce one. So the honest outcome asserted here is that the anonymous post is there, the encrypted
/// one is not, and nothing is logged as an error. Deliver the encrypted half and this test changes.
/// </para>
/// </remarks>
[TestFixture]
public class AppAcceptedConnectionFeedSyncTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AppAcceptedConnection_BackPopulatesTheFeedWithTheNewContactsSecuredPosts()
    {
        const int fileType = 7841;

        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Frodo's two channels and two posts: one anonymous, one behind his friends-only circle.
        var frodoFriendsOnlyCircle = Guid.NewGuid();
        var frodoFiles = await FeedScenario.PrepareIdentityWithChannelsAndPostsAsync(frodo, frodoFriendsOnlyCircle,
            postFileType: fileType,
            publicPostAcl: AccessControlList.Anonymous,
            friendsOnlyContent: "frodo's friends-only post",
            publicContent: "frodo's public post");

        // Sam follows first, unconnected: only the anonymous post can reach him.
        var follow = await sam.V1.Follower.FollowIdentity(frodo.Identity, FollowerNotificationType.AllNotifications, []);
        Assert.That(follow.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var beforeConnecting = await sam.V1.Drive.QueryBatch(FeedScenario.FeedQuery(fileType));
        Assert.That(beforeConnecting.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(beforeConnecting.Content!.SearchResults!.Count(), Is.EqualTo(1),
            "precondition: unconnected, Sam can only see Frodo's anonymous post -- if he already had both, " +
            "this test could not tell whether the accept-time sync did anything");

        // An app on Sam's side, holding what the accept endpoint needs and nothing more. Notably no
        // ManageFeed and no feed drive: that is the whole point -- it is the caller #1784 is about.
        var samAppDrive = TargetDrive.NewTargetDrive();
        await sam.Admin.CreateDrive(samAppDrive, "samAppDrive", allowAnonymousReads: false);
        var samApp = await AppSession.SetupAsync(sam, samAppDrive, DrivePermission.None,
            permissionKeys: new[]
            {
                PermissionKeys.ManageContacts,
                PermissionKeys.ReadConnectionRequests,
                PermissionKeys.ManageCircleMembership,
                PermissionKeys.UseTransitWrite
            });

        // Frodo asks to connect, granting Sam his friends-only circle -- that grant is what makes the
        // secured post readable, and it only exists from the moment the request is accepted.
        var request = await frodo.Connections.SendConnectionRequest(sam.Identity, [frodoFriendsOnlyCircle]);
        Assert.That(request.IsSuccessStatusCode, Is.True, $"SendConnectionRequest failed: {request.StatusCode}");

        var accept = await new V2ConnectionRequestsClient(samApp.Identity, samApp.Factory)
            .AcceptIncomingRequestAsync(frodo.Identity, new AcceptConnectionRequestV2());
        Assert.That(accept.IsSuccessStatusCode, Is.True, $"app accept failed: {accept.StatusCode} {accept.Error?.Content}");

        // What the accept-time sync can deliver, and what it cannot.
        var afterAccepting = await sam.V1.Drive.QueryBatch(FeedScenario.FeedQuery(fileType));
        Assert.That(afterAccepting.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var files = afterAccepting.Content!.SearchResults!.ToList();

        Assert.That(files, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(f =>
                !f.FileMetadata.IsEncrypted && f.FileMetadata.AppData.Content == frodoFiles.PublicFileContent),
            "the unencrypted post is writable without the feed drive's storage key, so it is here");

        Assert.That(files, Has.Exactly(0).Matches<SharedSecretEncryptedFileHeader>(f => f.FileMetadata.IsEncrypted),
            "the encrypted post needs the feed drive's storage key to seal, which an app-accepted " +
            "connection cannot produce -- it is deferred, not delivered, and that is the open half of #1784");
    }
}
