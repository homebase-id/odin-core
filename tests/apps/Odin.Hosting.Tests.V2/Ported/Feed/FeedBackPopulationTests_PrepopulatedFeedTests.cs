using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Apps;
using Odin.Services.DataSubscription.Follower;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Feed/FeedBackPopulationTests_PrepopulatedFeedTests.cs
///
/// Two identities that already follow each other — so each feed already holds the other's anonymous
/// post — then connect. Connecting re-runs the channel-file synchronization, which is what puts the
/// secured post into each feed alongside the one already there.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>Both the follow and the connection-request paths back-populate inline over
/// <c>IOdinHttpClientFactory</c>, which <c>TestPeerHttpClientFactory</c> replaces in-process. No
/// drain or inbox processing is needed, and the original had none either.</item>
/// <item>The original declared a <c>TestCases()</c> source that <b>nothing consumed</b> — the single
/// test is a plain <c>[Test]</c> with no parameters. The dead source is dropped; its one live row was
/// <c>OwnerClientContext(WellKnownAppDrives.FeedDrive)</c>, with two commented-out siblings
/// (<c>GuestWriteOnlyAccessToDrive</c> → Forbidden, <c>AppReadOnlyAccessToDrive</c> → NotFound).</item>
/// <item>Sam's and Frodo's <c>Prepare…IdentityWithChannelsAndPosts</c> helpers were copy-paste twins
/// differing only in the two content strings; both, and the twins the other feed fixtures carried,
/// are <see cref="FeedScenario.PrepareIdentityWithChannelsAndPostsAsync"/>.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — per-test reset
/// covers them. Carried defect worth recording even though the line is gone: the original's last
/// cleanup call was <c>ownerSam.Follower.UnfollowIdentity(sam.OdinId)</c>, i.e. Sam unfollowing
/// <i>himself</i> rather than Frodo, so Sam's follow leaked into whatever ran next.</item>
/// </list>
/// </remarks>
[TestFixture]
public class FeedBackPopulationTests_PrepopulatedFeedTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CanSynchronizeFeedFiles_WhenPreviouslyFollowedWheConnectionEstablished()
    {
        const int fileType = 1665;

        var ownerSam = await LoginAsOwner(Identities.Sam);
        var ownerFrodo = await LoginAsOwner(Identities.Frodo);

        var samFriendsOnlyCircle = Guid.NewGuid();
        var samPreparedFiles = await FeedScenario.PrepareIdentityWithChannelsAndPostsAsync(ownerSam, samFriendsOnlyCircle,
            postFileType: fileType,
            publicPostAcl: AccessControlList.Anonymous);

        var frodoFriendsOnlyCircle = Guid.NewGuid();
        var frodoPreparedFiles = await FeedScenario.PrepareIdentityWithChannelsAndPostsAsync(ownerFrodo, frodoFriendsOnlyCircle,
            postFileType: fileType,
            publicPostAcl: AccessControlList.Anonymous,
            friendsOnlyContent: "some secured friends only content from frodo",
            publicContent: "some public content from frodo");

        //
        // Precondition - both sam and frodo follow each other; therefore - they will have files in their feed drives already
        //
        await AssertFollowsAndGetsExpectedFilesAsync(ownerFrodo, ownerSam, fileType, samPreparedFiles);
        await AssertFollowsAndGetsExpectedFilesAsync(ownerSam, ownerFrodo, fileType, frodoPreparedFiles);

        //
        // Note: the Connection request process will call SynchronizedChannelFiles because they already follow each other
        //
        await ownerSam.Connections.SendConnectionRequest(ownerFrodo.Identity, [samFriendsOnlyCircle]);
        await ownerFrodo.Connections.AcceptConnectionRequest(ownerSam.Identity, [frodoFriendsOnlyCircle]);

        //
        // Validate frodo and Sam have secured files in their feeds
        //
        await FeedScenario.AssertHasAllExpectedFeedFilesAsync(ownerFrodo, FeedScenario.FeedQuery(fileType), samPreparedFiles);
        await FeedScenario.AssertHasAllExpectedFeedFilesAsync(ownerSam, FeedScenario.FeedQuery(fileType), frodoPreparedFiles);
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// <paramref name="follower"/> follows <paramref name="followee"/>; only the anonymous post lands,
    /// because they are not connected yet.
    /// </summary>
    private static async Task AssertFollowsAndGetsExpectedFilesAsync(
        OwnerSession follower,
        OwnerSession followee,
        int fileType,
        FeedScenario.PreparedFiles preparedFiles)
    {
        var followResponse = await follower.V1.Follower.FollowIdentity(followee.Identity,
            FollowerNotificationType.AllNotifications, []);
        Assert.That(followResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var queryFeedResponse = await follower.V1.Drive.QueryBatch(FeedScenario.FeedQuery(fileType));
        Assert.That(queryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = queryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(1));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == preparedFiles.PublicFileContent));
    }
}
