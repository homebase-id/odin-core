using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
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

        var samFriendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        var samFriendsOnlyCircle = Guid.NewGuid();
        var encryptedFriendsFileContent64 = await PrepareSamIdentityWithChannelsAndPostsAsync(ownerSam,
            samFriendsOnlyCircle, samFriendsOnlyTargetDrive, postFileType: fileType);

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
        var frodoQueryFeedResponse = await ownerFrodo.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = WellKnownAppDrives.FeedDrive,
                FileType = [fileType]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });

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
        var samQueryFeedResponse = await ownerSam.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = WellKnownAppDrives.FeedDrive,
                FileType = []
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });

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

    private static async Task<string> PrepareSamIdentityWithChannelsAndPostsAsync(
        OwnerSession samOwnerClient, Guid circleId, TargetDrive friendsOnlyTargetDrive, int postFileType)
    {
        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        await samOwnerClient.Admin.CreateDrive(friendsOnlyTargetDrive, "Secured Channel Drive",
            allowAnonymousReads: false, ownerOnly: false, allowSubscriptions: true);

        await samOwnerClient.Admin.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new()
                    {
                        Drive = friendsOnlyTargetDrive,
                        Permission = DrivePermission.Read
                    },
                }
            },
            PermissionSet = default
        });

        //
        // upload one post to friends target drive
        //
        const string friendsOnlyContent = "some secured friends only content";
        var friendsFile = SampleMetadataData.CreateWithContent(postFileType, friendsOnlyContent, AccessControlList.Connected);
        friendsFile.AllowDistribution = true;
        var (friendsFileUploadResponse, encryptedJsonContent64) = await samOwnerClient.V1.Drive.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile);

        Assert.That(friendsFileUploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return encryptedJsonContent64;
    }
}
