using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.DriveTests;

namespace Odin.Hosting.Tests._Universal.Feed;

public class Feed_Comment_Tests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
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
        // yield return new object[] { new AppReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
        yield return new object[] { new OwnerClientContext(SystemDriveConstants.FeedDrive), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    [Ignore("wip")]
    public async Task CommentingOnSecuredChannel_UpdatesReactionPreviewInCommentersFeed(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Sam and frodo are connected
        // Sam puts frodo in private channel
        // Sam posts to private channel
        // Frodo follows Sam
        // Frodo Comments
        // Frodo can query his feed and see reaction preview of 1 comment

        const int fileType = 1039;

        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var samFriendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        Guid samFriendsOnlyCircle = Guid.NewGuid();
        var encryptedFriendsFileContent64 = await PrepareSamIdentityWithChannelsAndPosts(samFriendsOnlyCircle, samFriendsOnlyTargetDrive, postFileType: fileType);

        var followSamResponse1 = await ownerFrodo.Follower.FollowIdentity(TestIdentities.Samwise.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

        // grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { samFriendsOnlyCircle });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { });

        // ownerFrodo.Transit
        await callerContext.Initialize(ownerFrodo);

        //at this point we follow sam 
        var followerApiClient = new UniversalFollowerApiClient(ownerFrodo.Identity.OdinId, callerContext.GetFactory());
        var followSamResponse = await followerApiClient.FollowIdentity(TestIdentities.Samwise.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

        ClassicAssert.IsTrue(followSamResponse.IsSuccessStatusCode, $"actual status code was {followSamResponse.StatusCode}");

        // Frodo will post on Sam's identity


        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var driveClient = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, callerContext.GetFactory());
        var frodoQueryFeedResponse = await driveClient.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = new FileQueryParamsV1()
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

        ClassicAssert.IsTrue(frodoQueryFeedResponse.StatusCode == expectedStatusCode, $"Actual code was {frodoQueryFeedResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults;
            ClassicAssert.IsNotNull(feedSearchResults);
            ClassicAssert.IsTrue(feedSearchResults.Count() == 2);

            var expectedFriendsOnlyFile = feedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted &&
                s.FileMetadata.AppData.Content == encryptedFriendsFileContent64);
            ClassicAssert.IsNotNull(expectedFriendsOnlyFile);


            //
            // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
            //

            var samQueryFeedResponse = await ownerSam.DriveRedux.QueryBatch(new QueryBatchRequest()
            {
                QueryParams = new FileQueryParamsV1()
                {
                    TargetDrive = SystemDriveConstants.FeedDrive,
                    FileType = new List<int>() { }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            });

            ClassicAssert.IsTrue(samQueryFeedResponse.IsSuccessStatusCode, $"Actual code was {samQueryFeedResponse.StatusCode}");
            var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults;
            ClassicAssert.IsNotNull(samFeedSearchResults);
            ClassicAssert.IsTrue(samFeedSearchResults.Count() == 2);

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

        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerFrodo.Follower.UnfollowIdentity(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
    }

    private async Task<string> PrepareSamIdentityWithChannelsAndPosts(Guid circleId, TargetDrive friendsOnlyTargetDrive, int postFileType)
    {
        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        await samOwnerClient.DriveManager.CreateDrive(friendsOnlyTargetDrive, "Secured Channel Drive", "", false, false, true);

        await samOwnerClient.Network.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
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
        var (friendsFileUploadResponse, encryptedJsonContent64) = await samOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile);

        ClassicAssert.IsTrue(friendsFileUploadResponse.IsSuccessStatusCode);

        return encryptedJsonContent64;
    }
}