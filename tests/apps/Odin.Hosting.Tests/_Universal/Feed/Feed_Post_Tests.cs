using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.HttpResults;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Membership.Circles;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.DriveTests;

namespace Odin.Hosting.Tests._Universal.Feed;

public class Feed_Post_Tests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }


    public static IEnumerable TestCases()
    {
        // yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        yield return new object[]
        {
            new AppSpecifyDriveAccess(SystemDriveConstants.PublicPostsChannelDrive, DrivePermission.ReadWrite, new TestPermissionKeyList(
                PermissionKeys.All.ToArray())),
            HttpStatusCode.OK
        };
        // yield return new object[] { new OwnerClientContext(SystemDriveConstants.FeedDrive), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanDistributeFeedFileToConnectedIdentity_OnPublicChannel_WhenFileAclTargetsCircle_And_RecipientCanDecrypt(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
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

        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var circleId = Guid.NewGuid();
        await ownerSam.Network.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest()
        {
            // No additional drive access is intentional as Frodo is in the SystemCircleConstants.ConnectedIdentitiesSystemCircleId
            Drives = default,
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        // Grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, [circleId]);
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, []);

        //at this point we follow sam 
        var followSamResponse = await ownerFrodo.Follower.FollowIdentity(TestIdentities.Samwise.OdinId, FollowerNotificationType.AllNotifications, []);

        Assert.IsTrue(followSamResponse.IsSuccessStatusCode, $"actual status code was {followSamResponse.StatusCode}");

        //
        // Using the feed app, Sam posts to public channel with encrypted file having ACL of friends circle
        //

        await callerContext.Initialize(ownerSam);
        var driveApiAsFeedApp = new UniversalDriveApiClient(ownerSam.Identity.OdinId, callerContext.GetFactory());

        var friendsFile = SampleMetadataData.CreateWithContent(fileType, friendsOnlyContent,
            acl: new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                CircleIdList = [circleId]
            });

        friendsFile.AllowDistribution = true;

        var (friendsFileUploadResponse, encryptedJsonContent64) = await driveApiAsFeedApp.UploadNewEncryptedMetadata(
            SystemDriveConstants.PublicPostsChannelDrive,
            friendsFile);

        Assert.IsTrue(friendsFileUploadResponse.StatusCode == expectedStatusCode, $"Actual code was {friendsFileUploadResponse.StatusCode}");
        
        await ownerSam.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.PublicPostsChannelDrive);

        await _scaffold.CreateOwnerApiClient(ownerFrodo.Identity).Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        //
        // Validation - check that frodo has 1 file in feed; from sam and he can decrypt it
        //
        var frodoQueryFeedResponse = await ownerFrodo.DriveRedux.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = SystemDriveConstants.FeedDrive,
                FileType = new List<int>() { fileType }
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });

        Assert.IsTrue(frodoQueryFeedResponse.IsSuccessStatusCode, $"Actual code was {frodoQueryFeedResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var file = frodoQueryFeedResponse.Content?.SearchResults?.SingleOrDefault();
            Assert.IsNotNull(file);

            Assert.IsTrue(file.FileMetadata.IsEncrypted);
            Assert.IsTrue(file.FileMetadata.AppData.Content == encryptedJsonContent64);

            var ss = ownerFrodo.GetTokenContext().SharedSecret;
            //frodo should be able to decrypt the file.
            var keyHeader = file.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

            var decryptedText = keyHeader.Decrypt(Convert.FromBase64String(file.FileMetadata.AppData.Content)).ToStringFromUtf8Bytes();
            Assert.IsTrue(decryptedText == friendsOnlyContent);
        }

        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerFrodo.Follower.UnfollowIdentity(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
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

        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var circleId = Guid.NewGuid();
        await ownerSam.Network.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest()
        {
            Drives =
            [
                new DriveGrantRequest()
                {
                    PermissionedDrive = new()
                    {
                        Drive = SystemDriveConstants.PublicPostsChannelDrive,
                        Permission = DrivePermission.Read
                    }
                }
            ],
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        // Grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { circleId });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { });

        var x = await ownerSam.Network.GetConnectionInfo(ownerFrodo.Identity.OdinId);
        Assert.IsNotNull(x);
        //
        // Using the feed app, Sam posts to public channel with encrypted file having ACL of friends circle
        //

        var feedAppCallerContext = new AppSpecifyDriveAccess(SystemDriveConstants.PublicPostsChannelDrive, DrivePermission.ReadWrite,
            new TestPermissionKeyList(PermissionKeys.All.ToArray()));

        await feedAppCallerContext.Initialize(ownerSam);
        var driveApiAsFeedApp = new UniversalDriveApiClient(ownerSam.Identity.OdinId, feedAppCallerContext.GetFactory());

        var friendsFile = SampleMetadataData.CreateWithContent(fileType, friendsOnlyContent,
            acl: new AccessControlList()
            {
                RequiredSecurityGroup = SecurityGroupType.Connected,
                CircleIdList = [circleId]
            });

        friendsFile.AllowDistribution = true;

        var (friendsFileUploadResponse, encryptedJsonContent64) = await driveApiAsFeedApp.UploadNewEncryptedMetadata(
            SystemDriveConstants.PublicPostsChannelDrive,
            friendsFile);
        Assert.IsTrue(friendsFileUploadResponse.IsSuccessStatusCode, $"Actual code was {friendsFileUploadResponse.StatusCode}");

        await ownerSam.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.PublicPostsChannelDrive);

        //validate sam can see his file

        var fileOnPublicDriveBatchRequest = new QueryBatchRequest()
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = SystemDriveConstants.PublicPostsChannelDrive,
                FileType = new List<int>() { fileType }
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        };

        //
        // First validate sam can see the file on his public drive
        //
        var samQueryFileOnHisPublicDriveResponse = await ownerSam.DriveRedux.QueryBatch(fileOnPublicDriveBatchRequest);
        var samFile = samQueryFileOnHisPublicDriveResponse.Content.SearchResults.SingleOrDefault();
        Assert.IsNotNull(samFile, "sam cannot see his own file");
        Assert.IsTrue(samFile.FileMetadata.AppData.FileType == fileType);

        await _scaffold.CreateOwnerApiClient(ownerFrodo.Identity).Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var driveGrants = new List<DriveGrantRequest>()
        {
            new DriveGrantRequest()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = SystemDriveConstants.PublicPostsChannelDrive,
                    Permission = DrivePermission.Read | DrivePermission.React | DrivePermission.Comment
                }
            }
        };
        // Login to Frodo's identity as Sam
        var frodoCallerContextOnSam = new GuestAccess(frodo.OdinId, driveGrants, [], new TestPermissionKeyList());

        await frodoCallerContextOnSam.Initialize(ownerSam);
        var guestDrive = new UniversalDriveApiClient(ownerSam.Identity.OdinId, frodoCallerContextOnSam.GetFactory());

        //
        // Validation - check that frodo can see the file on sam's public drive (remember - file ACL is for friend's only and frodo is in that circle)
        //
        var frodoQuerySamPublicChannelResponse = await guestDrive.QueryBatch(fileOnPublicDriveBatchRequest);

        Assert.IsTrue(frodoQuerySamPublicChannelResponse.IsSuccessStatusCode, $"Actual code was {frodoQuerySamPublicChannelResponse.StatusCode}");

        var file = frodoQuerySamPublicChannelResponse.Content?.SearchResults?.SingleOrDefault();
        Assert.IsNotNull(file);

        Assert.IsTrue(file.FileMetadata.IsEncrypted);
        Assert.IsTrue(file.FileMetadata.AppData.Content == encryptedJsonContent64);

        var ss = ownerFrodo.GetTokenContext().SharedSecret;
        //frodo should be able to decrypt the file.
        var keyHeader = file.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref ss);

        var decryptedText = keyHeader.Decrypt(Convert.FromBase64String(file.FileMetadata.AppData.Content)).ToStringFromUtf8Bytes();
        Assert.IsTrue(decryptedText == friendsOnlyContent);


        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
    }
}