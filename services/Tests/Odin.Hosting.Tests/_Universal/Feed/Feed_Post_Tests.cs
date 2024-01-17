using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.DataSubscription.Follower;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Membership.Circles;
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
        // Sam creates family circle
        // Sam puts frodo in Friends circle
        // Sam posts to public channel with encrypted file having ACL of family circle
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
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { circleId });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { });

        //at this point we follow sam 
        var followSamResponse = await ownerFrodo.Follower.FollowIdentity(TestIdentities.Samwise.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

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
            friendsFile,
            useGlobalTransitId: true);

        Assert.IsTrue(friendsFileUploadResponse.StatusCode == expectedStatusCode, $"Actual code was {friendsFileUploadResponse.StatusCode}");

        await ownerSam.Cron.DistributeFeedFiles();
        await ownerSam.Cron.ProcessTransitOutbox();

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
}