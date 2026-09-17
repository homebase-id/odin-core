using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Apps;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Feed/Feed_Comment_Tests.cs
///
/// Commenting on a secured channel should update the reaction preview in the commenter's feed.
/// </summary>
/// <remarks>
/// Checked port. <b>Entirely <c>[Ignore]("wip")</c>, carried as-is</b> — this moved without ever
/// having run.
/// <list type="bullet">
/// <item>The original's <c>TestCases()</c> had one live row,
/// <c>OwnerClientContext(WellKnownAppDrives.FeedDrive)</c>, with two commented-out siblings kept
/// verbatim below. One row is not a matrix, so this is a plain <c>[Test]</c> and the always-true
/// <c>if (expectedStatusCode == OK)</c> guard is gone.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// <item><c>PrepareSamIdentityWithChannelsAndPosts</c> was the friends-only half of the helper the
/// three other feed fixtures carried whole; it is
/// <see cref="FeedScenario.PrepareFriendsOnlyChannelAsync"/>, which creates the secured channel drive
/// itself — the original took one in only to have its caller create it and never look at it
/// again.</item>
/// <item>Carried defects, all left exactly as found:
/// the test never comments on anything, despite "Frodo Comments" in its own plan comment and
/// <c>Commenting…</c> in its name, and never reads a reaction preview;
/// it follows Sam twice, the first result (<c>followSamResponse1</c>) being assigned and never
/// looked at;
/// it asserts Frodo's feed holds 2 files although Sam prepared only one post;
/// and the whole Sam-side block asserts a count of 2 with its three content checks commented
/// out.</item>
/// </list>
/// </remarks>
[TestFixture]
public class Feed_Comment_Tests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // The original's live row was [OwnerClientContext(WellKnownAppDrives.FeedDrive), HttpStatusCode.OK]
    // — one row, so this is a plain [Test]. Its two commented-out siblings, kept verbatim:
    //   GuestWriteOnlyAccessToDrive -> HttpStatusCode.Forbidden
    //   AppReadOnlyAccessToDrive    -> HttpStatusCode.NotFound
    [Test]
    [Ignore("wip")]
    public async Task CommentingOnSecuredChannel_UpdatesReactionPreviewInCommentersFeed()
    {
        // Sam and frodo are connected
        // Sam puts frodo in private channel
        // Sam posts to private channel
        // Frodo follows Sam
        // Frodo Comments
        // Frodo can query his feed and see reaction preview of 1 comment

        const int fileType = 1039;

        var ownerSam = await LoginAsOwner(Identities.Sam);
        var ownerFrodo = await LoginAsOwner(Identities.Frodo);

        var samFriendsOnlyCircle = Guid.NewGuid();
        var encryptedFriendsFileContent64 = await FeedScenario.PrepareFriendsOnlyChannelAsync(ownerSam,
            samFriendsOnlyCircle, postFileType: fileType);

        var followSamResponse1 = await ownerFrodo.V1.Follower.FollowIdentity(ownerSam.Identity,
            FollowerNotificationType.AllNotifications,
            []);

        // grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(ownerFrodo.Identity, [samFriendsOnlyCircle]);
        await ownerFrodo.Connections.AcceptConnectionRequest(ownerSam.Identity, []);

        //at this point we follow sam
        var followSamResponse = await ownerFrodo.V1.Follower.FollowIdentity(ownerSam.Identity,
            FollowerNotificationType.AllNotifications,
            []);

        Assert.That(followSamResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        // Frodo will post on Sam's identity


        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var frodoQueryFeedResponse = await ownerFrodo.V1.Drive.QueryBatch(FeedScenario.FeedQuery(fileType));

        Assert.That(frodoQueryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(2));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == encryptedFriendsFileContent64));

        //
        // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var samQueryFeedResponse = await ownerSam.V1.Drive.QueryBatch(FeedScenario.FeedQuery([]));

        Assert.That(samQueryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(samFeedSearchResults, Is.Not.Null);
        Assert.That(samFeedSearchResults.Count, Is.EqualTo(2));

        // var samExpectedFriendsOnlyFile = samFeedSearchResults.SingleOrDefault(s =>
        //     s.FileMetadata.IsEncrypted &&
        //     s.FileMetadata.AppData.Content == frodoPreparedFiles.encryptedFriendsFileContent64);
        // ClassicAssert.IsNotNull(samExpectedFriendsOnlyFile);
        //
        // var samExpectedPublicFile = samFeedSearchResults.SingleOrDefault(s =>
        //     s.FileMetadata.IsEncrypted == false &&
        //     s.FileMetadata.AppData.Content == frodoPreparedFiles.publicFileContent);
        // ClassicAssert.IsNotNull(samExpectedPublicFile);
    }
}
