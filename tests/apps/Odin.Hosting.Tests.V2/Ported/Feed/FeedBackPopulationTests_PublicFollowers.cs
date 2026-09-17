using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Apps;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Feed/FeedBackPopulationTests_PublicFollowers.cs
///
/// Back-population for a follower who is <i>not</i> a connection: only the anonymous posts reach the
/// follower's feed drive; the encrypted post on the friends-only channel does not.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>Back-population is inline, not queued: the follow request calls
/// <c>FollowerService.SynchronizeChannelFilesAsync</c> over <c>IOdinHttpClientFactory</c>, which
/// <c>TestPeerHttpClientFactory</c> replaces in-process. No drain or inbox processing is needed, and
/// the original had none either.</item>
/// <item>The original's <c>TestCases()</c> had one live row,
/// <c>OwnerClientContext(WellKnownAppDrives.FeedDrive)</c>, with two commented-out siblings kept
/// verbatim below. One row is not a matrix, so this is a plain <c>[Test]</c> and the always-true
/// <c>if (expectedStatusCode == OK)</c> guard is gone.</item>
/// <item>The trailing unfollow was cleanup only and is dropped — per-test reset covers it.</item>
/// <item><c>PrepareSamIdentityWithChannelsAndPosts</c> was a near-twin of the same helper in the
/// three other feed fixtures; it is
/// <see cref="FeedScenario.PrepareIdentityWithChannelsAndPostsAsync"/> with
/// <c>extraPublicPosts: 3</c>. Carried defect, left as written: only the <i>first</i> of the four
/// public uploads is asserted, and only after the other three have already run; the three extra
/// posts that make the expected count 4 go unchecked.</item>
/// </list>
/// </remarks>
[TestFixture]
public class FeedBackPopulationTests_PublicFollowers : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // The original's live row was [OwnerClientContext(WellKnownAppDrives.FeedDrive), HttpStatusCode.OK]
    // — one row, so this is a plain [Test]. Its two commented-out siblings, kept verbatim:
    //   GuestWriteOnlyAccessToDrive -> HttpStatusCode.Forbidden
    //   AppReadOnlyAccessToDrive    -> HttpStatusCode.NotFound
    [Test]
    public async Task FollowingIdentity_PopulatesFollowersFeedWithAnonymousFiles()
    {
        const int fileType = 1038;

        var ownerSam = await LoginAsOwner(Identities.Sam);
        var ownerFrodo = await LoginAsOwner(Identities.Frodo);

        var friendsOnlyCircle = Guid.NewGuid();
        var samPreparedFiles = await FeedScenario.PrepareIdentityWithChannelsAndPostsAsync(ownerSam, friendsOnlyCircle,
            postFileType: fileType,
            publicPostAcl: AccessControlList.Anonymous,
            extraPublicPosts: 3);

        //at this point we follow sam
        var followSamResponse = await ownerFrodo.V1.Follower.FollowIdentity(ownerSam.Identity,
            FollowerNotificationType.AllNotifications,
            []);

        Assert.That(followSamResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        //Crucial point - we have to tell frodo's identity sync to sam after we call follow
        // await ownerFrodo.V1.Follower.SynchronizeFeed(ownerSam.Identity);

        //
        // Validation - check that frodo has 4 files in his feed; files are from Sam, none are encrypted
        //
        // maxRecords: the original left MaxRecords at its default of 100 here, unlike the other feed
        // fixtures, which ask for 10. Four files are expected either way.
        var frodoQueryFeedResponse = await ownerFrodo.V1.Drive.QueryBatch(
            FeedScenario.FeedQuery(fileType, maxRecords: 100));

        Assert.That(frodoQueryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(4));

        Assert.That(feedSearchResults, Has.Exactly(0).Matches<SharedSecretEncryptedFileHeader>(s =>
                s.FileMetadata.IsEncrypted &&
                s.FileMetadata.AppData.Content == samPreparedFiles.EncryptedFriendsFileContent64),
            "there should be no friend's only files");

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == samPreparedFiles.PublicFileContent));
    }
}
