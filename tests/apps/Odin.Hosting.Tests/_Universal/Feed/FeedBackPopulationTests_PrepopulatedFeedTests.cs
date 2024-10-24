using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Follower;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;

namespace Odin.Hosting.Tests._Universal.Feed;

//Tests to validate we can distribute older feed items to
//recipients (i.e. in the case of when we're first connected, I want my feed items to show in your feed)
public class FeedBackPopulationTests_PrepopulatedFeedTests
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
    public async Task CanSynchronizeFeedFiles_WhenPreviouslyFollowedWheConnectionEstablished()
    {
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

        //
        // Precondition - both sam and frodo follow each other; therefore - they will have files in their feed drives already
        //
        await AssertFrodoFollowsSamAndGetsExpectedFiles(ownerFrodo, sam, fileType, samPreparedFiles);
        await AssertSamFollowsFrodoAndGetsExpectedFiles(ownerSam, frodo, fileType, frodoPreparedFiles);
        
        //
        // Note: the Connection request process will call SynchronizedChannelFiles because they already follow each other 
        //
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { samFriendsOnlyCircle });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { frodoFriendsOnlyCircle });

        //
        // Validate frodo and Sam have secured files in their feeds
        //
        await AssertFrodoHasAllExpectedFeedFiles(ownerFrodo, fileType, samPreparedFiles);
        await AssertSamHasAllExpectedFeedFiles(ownerSam, fileType, frodoPreparedFiles);

        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerFrodo.Follower.UnfollowIdentity(sam.OdinId);
        
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
        await ownerSam.Follower.UnfollowIdentity(sam.OdinId);
    }

    private static async Task AssertSamHasAllExpectedFeedFiles(OwnerApiClientRedux ownerSam, int fileType,
        (string encryptedFriendsFileContent64, string publicFileContent) frodoPreparedFiles)
    {
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

        Assert.IsTrue(samQueryFeedResponse.IsSuccessStatusCode, $"Actual code was {samQueryFeedResponse.StatusCode}");
        var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.IsNotNull(samFeedSearchResults);
        Assert.IsTrue(samFeedSearchResults.Count() == 2);

        var samExpectedFriendsOnlyFile = samFeedSearchResults.SingleOrDefault(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == frodoPreparedFiles.encryptedFriendsFileContent64);
        Assert.IsNotNull(samExpectedFriendsOnlyFile);

        var samExpectedPublicFile = samFeedSearchResults.SingleOrDefault(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == frodoPreparedFiles.publicFileContent);
        Assert.IsNotNull(samExpectedPublicFile);
    }

    private static async Task AssertFrodoHasAllExpectedFeedFiles(OwnerApiClientRedux ownerFrodo, int fileType,
        (string encryptedFriendsFileContent64, string publicFileContent) samPreparedFiles)
    {
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

        Assert.IsTrue(frodoQueryFeedResponse.IsSuccessStatusCode);
        
        var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.IsNotNull(feedSearchResults);
        Assert.IsTrue(feedSearchResults.Count() == 2);

        var expectedFriendsOnlyFile = feedSearchResults.SingleOrDefault(s =>
            s.FileMetadata.IsEncrypted &&
            s.FileMetadata.AppData.Content == samPreparedFiles.encryptedFriendsFileContent64);
        Assert.IsNotNull(expectedFriendsOnlyFile);

        var expectedPublicFile = feedSearchResults.SingleOrDefault(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == samPreparedFiles.publicFileContent);
        Assert.IsNotNull(expectedPublicFile);
    }

    private static async Task AssertSamFollowsFrodoAndGetsExpectedFiles(OwnerApiClientRedux ownerSam, TestIdentity frodo, int fileType,
        (string encryptedFriendsFileContent64, string publicFileContent) frodoPreparedFiles)
    {
        var followResponse = await ownerSam.Follower.FollowIdentity(frodo.OdinId, FollowerNotificationType.AllNotifications, new List<TargetDrive>() { });
        Assert.IsTrue(followResponse.IsSuccessStatusCode);

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

        Assert.IsTrue(samQueryFeedResponse.IsSuccessStatusCode, $"Actual code was {samQueryFeedResponse.StatusCode}");
        var samFeedSearchResults = samQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.IsNotNull(samFeedSearchResults);
        Assert.IsTrue(samFeedSearchResults.Count() == 1);
        
        var samExpectedPublicFile = samFeedSearchResults.SingleOrDefault(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == frodoPreparedFiles.publicFileContent);
        Assert.IsNotNull(samExpectedPublicFile);
    }

    private static async Task AssertFrodoFollowsSamAndGetsExpectedFiles(OwnerApiClientRedux ownerFrodo, TestIdentity sam, int fileType,
        (string encryptedFriendsFileContent64, string publicFileContent) samPreparedFiles)
    {
        var followResponse = await ownerFrodo.Follower.FollowIdentity(sam.OdinId, FollowerNotificationType.AllNotifications, new List<TargetDrive>() { });

        Assert.IsTrue(followResponse.IsSuccessStatusCode);
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

        Assert.IsTrue(frodoQueryFeedResponse.IsSuccessStatusCode);

        var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults?.ToList();
        Assert.IsNotNull(feedSearchResults);
        Assert.IsTrue(feedSearchResults.Count() == 1);

        var expectedPublicFile = feedSearchResults.SingleOrDefault(s =>
            s.FileMetadata.IsEncrypted == false &&
            s.FileMetadata.AppData.Content == samPreparedFiles.publicFileContent);
        Assert.IsNotNull(expectedPublicFile);
    }


    private async Task<(string encryptedFriendsFileContent64, string publicFileContent)>
        PrepareSamIdentityWithChannelsAndPosts(Guid circleId, TargetDrive friendsOnlyTargetDrive, TargetDrive publicTargetDrive, int postFileType)
    {
        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to the friends (secured) channel drive
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

        Assert.IsTrue(friendsFileUploadResponse.response.IsSuccessStatusCode);

        //
        // upload one post to public target drive
        //
        const string publicContent = "some public content";
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Anonymous);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await samOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);

        Assert.IsTrue(publicFileUploadResult.IsSuccessStatusCode);

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }

    private async Task<(string encryptedFriendsFileContent64, string publicFileContent)>
        PrepareFrodoIdentityWithChannelsAndPosts(Guid circleId, TargetDrive friendsOnlyTargetDrive, TargetDrive publicTargetDrive, int postFileType)
    {
        // Frodo's identity creates the circle 'friends' with read access to a channel drive.
        // Frodo posts 1 item to this friends channel drive
        // Frodo posts 1 item to a public channel drive

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

        Assert.IsTrue(friendsFileUploadResponse.response.IsSuccessStatusCode);

        //
        // upload one post to public target drive
        //
        const string publicContent = "some public content from frodo";
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Anonymous);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await frodoOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);

        Assert.IsTrue(publicFileUploadResult.IsSuccessStatusCode);

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }
}