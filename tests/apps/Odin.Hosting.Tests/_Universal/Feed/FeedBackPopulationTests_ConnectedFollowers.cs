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

//Tests to validate we can distribute older feed items to
//recipients (i.e. in the case of when we're first connected, I want my feed items to show in your feed)
public class FeedBackPopulationTests_ConnectedFollowers
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
    public async Task FollowingIdentity_PopulatesConnectedFollowersFeedWithAnonymousAndSecuredFiles(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // what is the primary thing being tested here? - frodo's feed has 2 posts from sam, one secured, one public

        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        // Frodo sends connection request to Sam, Sam approves and puts Frodo in the friends circle
        // Frodo follows Sam

        // Upon following Sam, frodo requests back population

        const int fileType = 4579;

        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var samFriendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var samPublicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        Guid samFriendsOnlyCircle = Guid.NewGuid();
        var samPreparedFiles = await PrepareSamIdentityWithChannelsAndPosts(samFriendsOnlyCircle, samFriendsOnlyTargetDrive,
            samPublicTargetDrive,
            postFileType: fileType);

        var frodoFriendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var frodoPublicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        Guid frodoFriendsOnlyCircle = Guid.NewGuid();
        var frodoPreparedFiles = await PrepareFrodoIdentityWithChannelsAndPosts(frodoFriendsOnlyCircle, frodoFriendsOnlyTargetDrive,
            frodoPublicTargetDrive,
            postFileType: fileType);

        // grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { samFriendsOnlyCircle });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { frodoFriendsOnlyCircle });

        await callerContext.Initialize(ownerFrodo);

        //at this point we follow sam 
        var followerApiClient = new UniversalFollowerApiClient(ownerFrodo.Identity.OdinId, callerContext.GetFactory());
        var followSamResponse = await followerApiClient.FollowIdentity(TestIdentities.Samwise.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

        ClassicAssert.IsTrue(followSamResponse.IsSuccessStatusCode, $"actual status code was {followSamResponse.StatusCode}");

        var followFrodoResponse = await ownerSam.Follower.FollowIdentity(TestIdentities.Frodo.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

        ClassicAssert.IsTrue(followFrodoResponse.IsSuccessStatusCode, $"actual status code was {followFrodoResponse.StatusCode}");

        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var driveClient = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, callerContext.GetFactory());
        var frodoQueryFeedResponse = await driveClient.QueryBatch(new QueryBatchRequest()
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

        ClassicAssert.IsTrue(frodoQueryFeedResponse.StatusCode == expectedStatusCode, $"Actual code was {frodoQueryFeedResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults;
            ClassicAssert.IsNotNull(feedSearchResults);
            ClassicAssert.IsTrue(feedSearchResults.Count() == 2);

            var expectedFriendsOnlyFile = feedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted &&
                s.FileMetadata.AppData.Content == samPreparedFiles.encryptedFriendsFileContent64);
            ClassicAssert.IsNotNull(expectedFriendsOnlyFile);

            var expectedPublicFile = feedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted == false &&
                s.FileMetadata.AppData.Content == samPreparedFiles.publicFileContent);
            ClassicAssert.IsNotNull(expectedPublicFile);


            //
            // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
            //

            var samQueryFeedResponse = await ownerSam.DriveRedux.QueryBatch(new QueryBatchRequest()
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

            ClassicAssert.IsTrue(samQueryFeedResponse.IsSuccessStatusCode, $"Actual code was {samQueryFeedResponse.StatusCode}");
            var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults?.ToList();
            ClassicAssert.IsNotNull(samFeedSearchResults);
            ClassicAssert.IsTrue(samFeedSearchResults.Count() == 2);

            var samExpectedFriendsOnlyFile = samFeedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted &&
                s.FileMetadata.AppData.Content == frodoPreparedFiles.encryptedFriendsFileContent64);
            ClassicAssert.IsNotNull(samExpectedFriendsOnlyFile);

            var samExpectedPublicFile = samFeedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted == false &&
                s.FileMetadata.AppData.Content == frodoPreparedFiles.publicFileContent);
            ClassicAssert.IsNotNull(samExpectedPublicFile);
        }

        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerFrodo.Follower.UnfollowIdentity(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task ConnectToIdentity_PopulatesConnectedFollowersFeedWithAnonymousAndSecuredFiles(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // what is the primary thing being tested here? - frodo's feed has 2 posts from sam, one secured, one public

        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        // Frodo sends connection request to Sam, Sam approves and puts Frodo in the friends circle
        // Frodo follows Sam

        // Upon following Sam, frodo requests back population

        const int fileType = 1665;

        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var samFriendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var samPublicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        Guid samFriendsOnlyCircle = Guid.NewGuid();
        var samPreparedFiles = await PrepareSamIdentityWithChannelsAndPosts(samFriendsOnlyCircle, samFriendsOnlyTargetDrive,
            samPublicTargetDrive,
            postFileType: fileType);

        var frodoFriendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var frodoPublicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        Guid frodoFriendsOnlyCircle = Guid.NewGuid();
        var frodoPreparedFiles = await PrepareFrodoIdentityWithChannelsAndPosts(frodoFriendsOnlyCircle, frodoFriendsOnlyTargetDrive,
            frodoPublicTargetDrive,
            postFileType: fileType);

        // grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { samFriendsOnlyCircle });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { frodoFriendsOnlyCircle });

        await callerContext.Initialize(ownerFrodo);

        //at this point we follow sam 
        var followerApiClient = new UniversalFollowerApiClient(ownerFrodo.Identity.OdinId, callerContext.GetFactory());
        var followSamResponse = await followerApiClient.FollowIdentity(TestIdentities.Samwise.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

        ClassicAssert.IsTrue(followSamResponse.IsSuccessStatusCode, $"actual status code was {followSamResponse.StatusCode}");

        var followFrodoResponse = await ownerSam.Follower.FollowIdentity(TestIdentities.Frodo.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

        ClassicAssert.IsTrue(followFrodoResponse.IsSuccessStatusCode, $"actual status code was {followFrodoResponse.StatusCode}");

        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        var driveClient = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, callerContext.GetFactory());
        var frodoQueryFeedResponse = await driveClient.QueryBatch(new QueryBatchRequest()
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

        ClassicAssert.IsTrue(frodoQueryFeedResponse.StatusCode == expectedStatusCode, $"Actual code was {frodoQueryFeedResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults;
            ClassicAssert.IsNotNull(feedSearchResults);
            ClassicAssert.IsTrue(feedSearchResults.Count() == 2);

            var expectedFriendsOnlyFile = feedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted &&
                s.FileMetadata.AppData.Content == samPreparedFiles.encryptedFriendsFileContent64);
            ClassicAssert.IsNotNull(expectedFriendsOnlyFile);

            var expectedPublicFile = feedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted == false &&
                s.FileMetadata.AppData.Content == samPreparedFiles.publicFileContent);
            ClassicAssert.IsNotNull(expectedPublicFile);


            //
            // Validation - check that SAM has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
            //

            var samQueryFeedResponse = await ownerSam.DriveRedux.QueryBatch(new QueryBatchRequest()
            {
                QueryParams = new FileQueryParams()
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
            var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults?.ToList();
            ClassicAssert.IsNotNull(samFeedSearchResults);
            ClassicAssert.IsTrue(samFeedSearchResults.Count() == 2);

            var samExpectedFriendsOnlyFile = samFeedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted &&
                s.FileMetadata.AppData.Content == frodoPreparedFiles.encryptedFriendsFileContent64);
            ClassicAssert.IsNotNull(samExpectedFriendsOnlyFile);

            var samExpectedPublicFile = samFeedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted == false &&
                s.FileMetadata.AppData.Content == frodoPreparedFiles.publicFileContent);
            ClassicAssert.IsNotNull(samExpectedPublicFile);
        }

        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerFrodo.Follower.UnfollowIdentity(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
        await ownerSam.Follower.UnfollowIdentity(frodo.OdinId);
    }


    private async Task<(string encryptedFriendsFileContent64, string publicFileContent)>
        PrepareSamIdentityWithChannelsAndPosts(Guid circleId, TargetDrive friendsOnlyTargetDrive, TargetDrive publicTargetDrive, int postFileType)
    {
        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        await samOwnerClient.DriveManager.CreateDrive(publicTargetDrive, "Public Channel Drive", "", allowAnonymousReads: true, false,
            allowSubscriptions: true);
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
        var friendsFileUploadResponse = await samOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile);

        ClassicAssert.IsTrue(friendsFileUploadResponse.response.IsSuccessStatusCode);

        //
        // upload one post to public target drive
        //
        const string publicContent = "some public content";
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Connected);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await samOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);

        ClassicAssert.IsTrue(publicFileUploadResult.IsSuccessStatusCode);

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }

    private async Task<(string encryptedFriendsFileContent64, string publicFileContent)>
        PrepareFrodoIdentityWithChannelsAndPosts(Guid circleId, TargetDrive friendsOnlyTargetDrive, TargetDrive publicTargetDrive, int postFileType)
    {
        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        await frodoOwnerClient.DriveManager.CreateDrive(publicTargetDrive, "Public Channel Drive", "", allowAnonymousReads: true, false,
            allowSubscriptions: true);
        await frodoOwnerClient.DriveManager.CreateDrive(friendsOnlyTargetDrive, "Secured Channel Drive", "", false, false, true);

        await frodoOwnerClient.Network.CreateCircle(circleId, "Friends Only", new PermissionSetGrantRequest()
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
        const string friendsOnlyContent = "some secured friends only content from frodo";
        var friendsFile = SampleMetadataData.CreateWithContent(postFileType, friendsOnlyContent, AccessControlList.Connected);
        friendsFile.AllowDistribution = true;
        var friendsFileUploadResponse = await frodoOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile);

        ClassicAssert.IsTrue(friendsFileUploadResponse.response.IsSuccessStatusCode);

        //
        // upload one post to public target drive
        //
        const string publicContent = "some public content from frodo";
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Connected);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await frodoOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);

        ClassicAssert.IsTrue(publicFileUploadResult.IsSuccessStatusCode);

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }
}