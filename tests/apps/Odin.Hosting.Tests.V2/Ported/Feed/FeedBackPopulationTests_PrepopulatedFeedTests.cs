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
/// differing only in the two content strings; they are one parameterized helper here.</item>
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
        var samPreparedFiles = await PrepareIdentityWithChannelsAndPostsAsync(ownerSam, samFriendsOnlyCircle,
            postFileType: fileType,
            friendsOnlyContent: "some secured friends only content",
            publicContent: "some public content");

        var frodoFriendsOnlyCircle = Guid.NewGuid();
        var frodoPreparedFiles = await PrepareIdentityWithChannelsAndPostsAsync(ownerFrodo, frodoFriendsOnlyCircle,
            postFileType: fileType,
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
        await AssertHasAllExpectedFeedFilesAsync(ownerFrodo, fileType, samPreparedFiles);
        await AssertHasAllExpectedFeedFilesAsync(ownerSam, fileType, frodoPreparedFiles);
    }

    // ---------------------------------------------------------------------------------------------

    private static QueryBatchRequest FeedQuery(int fileType) => new()
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
    };

    /// <summary>Both posts — the anonymous one and the secured one — are in the follower's feed.</summary>
    private static async Task AssertHasAllExpectedFeedFilesAsync(
        OwnerSession follower,
        int fileType,
        (string EncryptedFriendsFileContent64, string PublicFileContent) preparedFiles)
    {
        var queryFeedResponse = await follower.V1.Drive.QueryBatch(FeedQuery(fileType));
        Assert.That(queryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = queryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(2));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == preparedFiles.EncryptedFriendsFileContent64));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == preparedFiles.PublicFileContent));
    }

    /// <summary>
    /// <paramref name="follower"/> follows <paramref name="followee"/>; only the anonymous post lands,
    /// because they are not connected yet.
    /// </summary>
    private static async Task AssertFollowsAndGetsExpectedFilesAsync(
        OwnerSession follower,
        OwnerSession followee,
        int fileType,
        (string EncryptedFriendsFileContent64, string PublicFileContent) preparedFiles)
    {
        var followResponse = await follower.V1.Follower.FollowIdentity(followee.Identity,
            FollowerNotificationType.AllNotifications, []);
        Assert.That(followResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var queryFeedResponse = await follower.V1.Drive.QueryBatch(FeedQuery(fileType));
        Assert.That(queryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = queryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(1));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == preparedFiles.PublicFileContent));
    }

    /// <summary>
    /// The identity creates the circle 'friends' with read access to a secured channel drive, posts one
    /// encrypted item to it, and posts one anonymous item to a public channel drive.
    /// </summary>
    private static async Task<(string EncryptedFriendsFileContent64, string PublicFileContent)>
        PrepareIdentityWithChannelsAndPostsAsync(
            OwnerSession owner,
            Guid circleId,
            int postFileType,
            string friendsOnlyContent,
            string publicContent)
    {
        var friendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var publicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        await owner.Admin.CreateDrive(publicTargetDrive, "Public Channel Drive", allowAnonymousReads: true,
            ownerOnly: false, allowSubscriptions: true);
        await owner.Admin.CreateDrive(friendsOnlyTargetDrive, "Secured Channel Drive", allowAnonymousReads: false,
            ownerOnly: false, allowSubscriptions: true);

        await owner.Admin.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest
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
        var friendsFile = SampleMetadataData.CreateWithContent(postFileType, friendsOnlyContent, AccessControlList.Connected);
        friendsFile.AllowDistribution = true;
        var friendsFileUploadResponse = await owner.V1.Drive.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile);

        Assert.That(friendsFileUploadResponse.response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // upload one post to public target drive
        //
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Anonymous);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await owner.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);

        Assert.That(publicFileUploadResult.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }
}
