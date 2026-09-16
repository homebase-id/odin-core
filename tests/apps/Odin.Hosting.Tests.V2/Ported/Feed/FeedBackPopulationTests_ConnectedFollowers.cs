using System;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Feed/FeedBackPopulationTests_ConnectedFollowers.cs
///
/// Back-population for a follower who is also a connection: on following, the follower's feed drive
/// is filled in with the followee's existing posts — both the anonymous one and the encrypted one on
/// a channel drive the follower's circle can read.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>Back-population is inline, not queued: the follow request calls
/// <c>FollowerService.SynchronizeChannelFilesAsync</c> over <c>IOdinHttpClientFactory</c>, which
/// <c>TestPeerHttpClientFactory</c> replaces in-process. No drain or inbox processing is needed, and
/// the originals had none either.</item>
/// <item>The original's <c>TestCases()</c> had one live row,
/// <c>OwnerClientContext(WellKnownAppDrives.FeedDrive)</c>, with two commented-out siblings kept
/// verbatim below. One row is not a matrix, so these are plain <c>[Test]</c>s and the always-true
/// <c>if (expectedStatusCode == OK)</c> guard is gone.</item>
/// <item>Sam's and Frodo's <c>Prepare…IdentityWithChannelsAndPosts</c> helpers were copy-paste twins
/// differing only in the two content strings; both, and the twins the other feed fixtures carried,
/// are <see cref="FeedScenario.PrepareIdentityWithChannelsAndPostsAsync"/>.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// <item>Carried defect: the two tests are the same test. <c>ConnectToIdentity_…</c> connects and
/// <i>then</i> follows, exactly as <c>FollowingIdentity_…</c> does, so neither exercises the
/// connect-after-follow path its name suggests; the only differences are the file type and that
/// <c>ConnectToIdentity_…</c> queries Sam's feed with an empty <c>FileType</c> filter instead of the
/// test's own file type. Both are carried as written.</item>
/// </list>
/// </remarks>
[TestFixture]
public class FeedBackPopulationTests_ConnectedFollowers : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // The original's live row was [OwnerClientContext(WellKnownAppDrives.FeedDrive), HttpStatusCode.OK]
    // — one row, so these are plain [Test]s. Its two commented-out siblings, kept verbatim:
    //   GuestWriteOnlyAccessToDrive -> HttpStatusCode.Forbidden
    //   AppReadOnlyAccessToDrive    -> HttpStatusCode.NotFound
    [Test]
    public async Task FollowingIdentity_PopulatesConnectedFollowersFeedWithAnonymousAndSecuredFiles()
    {
        // what is the primary thing being tested here? - frodo's feed has 2 posts from sam, one secured, one public

        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        // Frodo sends connection request to Sam, Sam approves and puts Frodo in the friends circle
        // Frodo follows Sam

        // Upon following Sam, frodo requests back population

        const int fileType = 4579;

        var (ownerSam, samPreparedFiles, ownerFrodo, frodoPreparedFiles) = await ConnectAndFollowAsync(fileType);

        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        await FeedScenario.AssertHasAllExpectedFeedFilesAsync(ownerFrodo, FeedScenario.FeedQuery(fileType), samPreparedFiles);

        //
        // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        await FeedScenario.AssertHasAllExpectedFeedFilesAsync(ownerSam, FeedScenario.FeedQuery(fileType), frodoPreparedFiles);
    }

    [Test]
    public async Task ConnectToIdentity_PopulatesConnectedFollowersFeedWithAnonymousAndSecuredFiles()
    {
        // what is the primary thing being tested here? - frodo's feed has 2 posts from sam, one secured, one public

        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        // Frodo sends connection request to Sam, Sam approves and puts Frodo in the friends circle
        // Frodo follows Sam

        // Upon following Sam, frodo requests back population

        const int fileType = 1665;

        var (ownerSam, samPreparedFiles, ownerFrodo, frodoPreparedFiles) = await ConnectAndFollowAsync(fileType);

        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        await FeedScenario.AssertHasAllExpectedFeedFilesAsync(ownerFrodo, FeedScenario.FeedQuery(fileType), samPreparedFiles);

        //
        // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        // Note (carried): unlike the test above, this query filters on no file type at all.
        await FeedScenario.AssertHasAllExpectedFeedFilesAsync(ownerSam, FeedScenario.FeedQuery([]), frodoPreparedFiles);
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Sam and Frodo each prepare their two channels and one post apiece, connect — each granting the
    /// other its own friends-only circle — and then follow each other.
    /// </summary>
    private async Task<(OwnerSession Sam, FeedScenario.PreparedFiles SamFiles,
            OwnerSession Frodo, FeedScenario.PreparedFiles FrodoFiles)>
        ConnectAndFollowAsync(int fileType)
    {
        var ownerSam = await LoginAsOwner(Identities.Sam);
        var ownerFrodo = await LoginAsOwner(Identities.Frodo);

        var samFriendsOnlyCircle = Guid.NewGuid();
        var samPreparedFiles = await FeedScenario.PrepareIdentityWithChannelsAndPostsAsync(ownerSam, samFriendsOnlyCircle,
            postFileType: fileType,
            publicPostAcl: AccessControlList.Connected);

        var frodoFriendsOnlyCircle = Guid.NewGuid();
        var frodoPreparedFiles = await FeedScenario.PrepareIdentityWithChannelsAndPostsAsync(ownerFrodo, frodoFriendsOnlyCircle,
            postFileType: fileType,
            publicPostAcl: AccessControlList.Connected,
            friendsOnlyContent: "some secured friends only content from frodo",
            publicContent: "some public content from frodo");

        // grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(ownerFrodo.Identity, [samFriendsOnlyCircle]);
        await ownerFrodo.Connections.AcceptConnectionRequest(ownerSam.Identity, [frodoFriendsOnlyCircle]);

        //at this point we follow sam
        var followSamResponse = await ownerFrodo.V1.Follower.FollowIdentity(ownerSam.Identity,
            FollowerNotificationType.AllNotifications,
            []);

        Assert.That(followSamResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var followFrodoResponse = await ownerSam.V1.Follower.FollowIdentity(ownerFrodo.Identity,
            FollowerNotificationType.AllNotifications,
            []);

        Assert.That(followFrodoResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        return (ownerSam, samPreparedFiles, ownerFrodo, frodoPreparedFiles);
    }
}
