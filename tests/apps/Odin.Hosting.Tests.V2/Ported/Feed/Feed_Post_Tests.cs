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
using Odin.Services.Authorization.Permissions;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;

namespace Odin.Hosting.Tests.V2.Ported.Feed;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/_Universal/Feed/Feed_Post_Tests.cs
///
/// A post the feed app writes to the public channel drive while the file's own ACL narrows it to one
/// circle: the connected follower in that circle gets it in their feed and can decrypt it.
/// </summary>
/// <remarks>
/// Checked port.
/// <list type="bullet">
/// <item>The original's <c>TestCases()</c> had one live row —
/// <c>AppSpecifyDriveAccess(PublicPostsChannelDrive, ReadWrite, TestPermissionKeyList(PermissionKeys.All))</c>
/// — with two commented-out siblings kept verbatim below. One row is not a matrix, so the first test
/// is a plain <c>[Test]</c> that builds that same App caller inline, and the always-true
/// <c>if (expectedStatusCode == OK)</c> guard is gone.</item>
/// <item><c>SetupCallerWithOwner</c> is not used: the original registered the app only after the
/// circle, connection and follow were in place, and the caller is built at that same point here.
/// <c>PublicPostsChannelDrive</c> is a system drive that <c>InitializeIdentity</c> has already
/// created, so nothing needs creating either.</item>
/// <item><c>WaitForEmptyOutbox</c> + <c>Transit.ProcessInbox(FeedDrive)</c> become
/// <c>Sync.DrainOutboxAsync()</c> + <c>Sync.ProcessInboxAsync(FeedDrive)</c>: unlike back-population,
/// live distribution of a new post <i>is</i> queued, so the drain is load-bearing here. The V1 calls
/// are passive polls on the outbox background service, which the fast host registers but never
/// starts.</item>
/// <item><c>GetTokenContext().SharedSecret</c> becomes <see cref="OwnerSession.SharedSecret"/>.</item>
/// <item><c>CanPostFile_ToPublicChannel_…_InGuestApi</c> keeps its <c>[Ignore]("wip")</c>. Its
/// <c>GuestAccess(frodo.OdinId, …)</c> — a YouAuth domain named for Frodo's own identity — becomes an
/// ordinary <see cref="CallerSpec.Guest"/>, whose domain is a generated one; the grant (Read | React
/// | Comment on the public channel drive, no permission keys) is the same. The test does not run, so
/// this deviation has not been exercised.</item>
/// <item>Trailing disconnect / unfollow calls were cleanup only and are dropped — per-test reset
/// covers them.</item>
/// </list>
/// </remarks>
[TestFixture]
public class Feed_Post_Tests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    // The original's live row was the AppSpecifyDriveAccess one built inline below. Its one
    // commented-out sibling, kept verbatim:
    //   GuestWriteOnlyAccessToDrive -> HttpStatusCode.Forbidden
    // …alongside a commented-out OwnerClientContext(WellKnownAppDrives.FeedDrive) -> HttpStatusCode.OK
    [Test]
    public async Task CanDistributeFeedFileToConnectedIdentity_OnPublicChannel_WhenFileAclTargetsCircle_And_RecipientCanDecrypt()
    {
        // Using feed as an app
        // System Circle has READ access to public drive
        // Sam and frodo are connected
        // Sam creates Friends circle
        // Sam puts frodo in Friends circle
        // Sam posts to public channel with encrypted file having ACL of Friends circle
        // Frodo follows Sam
        // Sam's identity distributes post
        // Frodo can see post in Frodo's feed and decrypt

        const int fileType = 1044;
        const string friendsOnlyContent = "some secured friends only content";

        var ownerSam = await LoginAsOwner(Identities.Sam);
        var ownerFrodo = await LoginAsOwner(Identities.Frodo);

        var circleId = Guid.NewGuid();
        await ownerSam.Admin.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest
        {
            // No additional drive access is intentional as Frodo is in the SystemCircleConstants.ConnectedIdentitiesSystemCircleId
            Drives = default,
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        // Grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(ownerFrodo.Identity, [circleId]);
        await ownerFrodo.Connections.AcceptConnectionRequest(ownerSam.Identity, []);

        //at this point we follow sam
        var followSamResponse = await ownerFrodo.V1.Follower.FollowIdentity(ownerSam.Identity,
            FollowerNotificationType.AllNotifications, []);

        Assert.That(followSamResponse.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        //
        // Using the feed app, Sam posts to public channel with encrypted file having ACL of friends circle
        //
        var feedAppSpec = CallerSpec.App(new DriveSpec(WellKnownAppDrives.PublicPostsChannelDrive),
            DrivePermission.ReadWrite, PermissionKeys.All.ToArray());
        var driveApiAsFeedApp = (await feedAppSpec.Build(ownerSam)).V1.Drive;

        var friendsFile = SampleMetadataData.CreateWithContent(fileType, friendsOnlyContent,
            acl: new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                CircleIdList = [circleId]
            });

        friendsFile.AllowDistribution = true;

        var (friendsFileUploadResponse, encryptedJsonContent64) = await driveApiAsFeedApp.UploadNewEncryptedMetadata(
            WellKnownAppDrives.PublicPostsChannelDrive,
            friendsFile);

        Assert.That(friendsFileUploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await ownerSam.Sync.DrainOutboxAsync();

        await ownerFrodo.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        //
        // Validation - check that frodo has 1 file in feed; from sam and he can decrypt it
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

        var file = frodoQueryFeedResponse.Content?.SearchResults?.SingleOrDefault();
        Assert.That(file, Is.Not.Null);

        Assert.That(file.FileMetadata.IsEncrypted, Is.True);
        Assert.That(file.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));

        var ss = ownerFrodo.SharedSecret;
        //frodo should be able to decrypt the file.
        var keyHeader = file.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

        var decryptedText = keyHeader.Decrypt(Convert.FromBase64String(file.FileMetadata.AppData.Content)).ToStringFromUtf8Bytes();
        Assert.That(decryptedText, Is.EqualTo(friendsOnlyContent));
    }

    [Test]
    [Ignore("wip")]
    public async Task CanPostFile_ToPublicChannel_WhenFileAclTargetsCircle_And_RecipientCanDecrypt_InGuestApi()
    {
        // Using feed as an app
        // System Circle has READ access to public drive
        // Sam and frodo are connected
        // Sam creates Friends circle
        // Sam puts frodo in Friends circle
        // Sam posts to public channel with encrypted file having ACL of Friends circle
        // Frodo logs in to Sam's identity using guest API
        // Frodo can see post in Sam's public channel


        //TODO: validate the public drive is granted by default.. at least read access
        // must you grant explicit access to the public drive for the circle to have the storage key?

        const int fileType = 1048;
        const string friendsOnlyContent = "some secured friends only content";

        var ownerSam = await LoginAsOwner(Identities.Sam);
        var ownerFrodo = await LoginAsOwner(Identities.Frodo);

        var circleId = Guid.NewGuid();
        await ownerSam.Admin.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest
        {
            Drives =
            [
                new DriveGrantRequest
                {
                    PermissionedDrive = new()
                    {
                        Drive = WellKnownAppDrives.PublicPostsChannelDrive,
                        Permission = DrivePermission.Read
                    }
                }
            ],
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        // Grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(ownerFrodo.Identity, [circleId]);
        await ownerFrodo.Connections.AcceptConnectionRequest(ownerSam.Identity, []);

        var x = await ownerSam.Connections.GetConnectionInfo(ownerFrodo.Identity);
        Assert.That(x, Is.Not.Null);
        //
        // Using the feed app, Sam posts to public channel with encrypted file having ACL of friends circle
        //
        var feedAppSpec = CallerSpec.App(new DriveSpec(WellKnownAppDrives.PublicPostsChannelDrive),
            DrivePermission.ReadWrite, PermissionKeys.All.ToArray());
        var driveApiAsFeedApp = (await feedAppSpec.Build(ownerSam)).V1.Drive;

        var friendsFile = SampleMetadataData.CreateWithContent(fileType, friendsOnlyContent,
            acl: new AccessControlList
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                CircleIdList = [circleId]
            });

        friendsFile.AllowDistribution = true;

        var (friendsFileUploadResponse, encryptedJsonContent64) = await driveApiAsFeedApp.UploadNewEncryptedMetadata(
            WellKnownAppDrives.PublicPostsChannelDrive,
            friendsFile);
        Assert.That(friendsFileUploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        await ownerSam.Sync.DrainOutboxAsync();

        //validate sam can see his file

        var fileOnPublicDriveBatchRequest = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1
            {
                TargetDrive = WellKnownAppDrives.PublicPostsChannelDrive,
                FileType = [fileType]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        //
        // First validate sam can see the file on his public drive
        //
        var samQueryFileOnHisPublicDriveResponse = await ownerSam.V1.Drive.QueryBatch(fileOnPublicDriveBatchRequest);
        var samFile = samQueryFileOnHisPublicDriveResponse.Content.SearchResults.SingleOrDefault();
        Assert.That(samFile, Is.Not.Null, "sam cannot see his own file");
        Assert.That(samFile.FileMetadata.AppData.FileType, Is.EqualTo(fileType));

        await ownerFrodo.Sync.ProcessInboxAsync(WellKnownAppDrives.FeedDrive);

        // Login to Frodo's identity as Sam
        var frodoCallerContextOnSam = CallerSpec.Guest(new DriveSpec(WellKnownAppDrives.PublicPostsChannelDrive),
            DrivePermission.Read | DrivePermission.React | DrivePermission.Comment);

        var guestDrive = (await frodoCallerContextOnSam.Build(ownerSam)).V1.Drive;

        //
        // Validation - check that frodo can see the file on sam's public drive (remember - file ACL is for friend's only and frodo is in that circle)
        //
        var frodoQuerySamPublicChannelResponse = await guestDrive.QueryBatch(fileOnPublicDriveBatchRequest);

        Assert.That(frodoQuerySamPublicChannelResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var file = frodoQuerySamPublicChannelResponse.Content?.SearchResults?.SingleOrDefault();
        Assert.That(file, Is.Not.Null);

        Assert.That(file.FileMetadata.IsEncrypted, Is.True);
        Assert.That(file.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));

        var ss = ownerFrodo.SharedSecret;
        //frodo should be able to decrypt the file.
        var keyHeader = file.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

        var decryptedText = keyHeader.Decrypt(Convert.FromBase64String(file.FileMetadata.AppData.Content)).ToStringFromUtf8Bytes();
        Assert.That(decryptedText, Is.EqualTo(friendsOnlyContent));
    }
}
