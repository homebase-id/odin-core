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
/// <item>Carried defect: <c>PrepareSamIdentityWithChannelsAndPosts</c> asserts only the <i>first</i>
/// of its four public uploads, and does so after the other three have already run; the three extra
/// posts that make the expected count 4 go unchecked. Left as written.</item>
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
        var samPreparedFiles = await PrepareSamIdentityWithChannelsAndPostsAsync(ownerSam, friendsOnlyCircle,
            postFileType: fileType);

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
        var frodoQueryFeedResponse = await ownerFrodo.V1.Drive.QueryBatch(new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = WellKnownAppDrives.FeedDrive,
                FileType = [fileType]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                IncludeMetadataHeader = true
            }
        });

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

    private static async Task<(string EncryptedFriendsFileContent64, string PublicFileContent)>
        PrepareSamIdentityWithChannelsAndPostsAsync(OwnerSession samOwnerClient, Guid circleId, int postFileType)
    {
        var friendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var publicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        await samOwnerClient.Admin.CreateDrive(publicTargetDrive, "Public Channel Drive", allowAnonymousReads: true,
            ownerOnly: false, allowSubscriptions: true);
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
        var friendsFileUploadResponse = await samOwnerClient.V1.Drive.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile);

        Assert.That(friendsFileUploadResponse.response.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        //
        // upload one post to public target drive
        //
        const string publicContent = "some public content";
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Anonymous);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await samOwnerClient.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);

        publicFile.AppData.Content = Guid.NewGuid().ToString();
        await samOwnerClient.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);

        publicFile.AppData.Content = Guid.NewGuid().ToString();
        await samOwnerClient.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);

        publicFile.AppData.Content = Guid.NewGuid().ToString();
        await samOwnerClient.V1.Drive.UploadNewMetadata(publicTargetDrive, publicFile);

        Assert.That(publicFileUploadResult.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }
}
