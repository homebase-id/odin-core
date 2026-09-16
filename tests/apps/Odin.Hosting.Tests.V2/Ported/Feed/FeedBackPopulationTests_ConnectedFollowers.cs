using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
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
/// differing only in the two content strings; they are one parameterized helper here.</item>
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

        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var frodoQueryFeedResponse = await ownerFrodo.V1.Drive.QueryBatch(FeedQuery([fileType]));
        Assert.That(frodoQueryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(2));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == samPreparedFiles.EncryptedFriendsFileContent64));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == samPreparedFiles.PublicFileContent));

        //
        // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var samQueryFeedResponse = await ownerSam.V1.Drive.QueryBatch(FeedQuery([fileType]));
        Assert.That(samQueryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(samFeedSearchResults, Is.Not.Null);
        Assert.That(samFeedSearchResults.Count, Is.EqualTo(2));

        Assert.That(samFeedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == frodoPreparedFiles.EncryptedFriendsFileContent64));

        Assert.That(samFeedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == frodoPreparedFiles.PublicFileContent));
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

        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var frodoQueryFeedResponse = await ownerFrodo.V1.Drive.QueryBatch(FeedQuery([fileType]));
        Assert.That(frodoQueryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(feedSearchResults, Is.Not.Null);
        Assert.That(feedSearchResults.Count, Is.EqualTo(2));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == samPreparedFiles.EncryptedFriendsFileContent64));

        Assert.That(feedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == samPreparedFiles.PublicFileContent));

        //
        // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        // Note (carried): unlike the test above, this query filters on no file type at all.
        var samQueryFeedResponse = await ownerSam.V1.Drive.QueryBatch(FeedQuery([]));
        Assert.That(samQueryFeedResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.That(samFeedSearchResults, Is.Not.Null);
        Assert.That(samFeedSearchResults.Count, Is.EqualTo(2));

        Assert.That(samFeedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == frodoPreparedFiles.EncryptedFriendsFileContent64));

        Assert.That(samFeedSearchResults, Has.Exactly(1).Matches<SharedSecretEncryptedFileHeader>(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == frodoPreparedFiles.PublicFileContent));
    }

    // ---------------------------------------------------------------------------------------------

    private static QueryBatchRequest FeedQuery(List<int> fileTypes) => new()
    {
        QueryParams = new FileQueryParamsV1
        {
            TargetDrive = WellKnownAppDrives.FeedDrive,
            FileType = fileTypes
        },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest
        {
            MaxRecords = 10,
            IncludeMetadataHeader = true
        }
    };

    /// <summary>
    /// The identity creates the circle 'friends' with read access to a secured channel drive, posts
    /// one encrypted item to it, and posts one unencrypted item to a public channel drive.
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
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Connected);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await owner.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);

        Assert.That(publicFileUploadResult.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }
}
