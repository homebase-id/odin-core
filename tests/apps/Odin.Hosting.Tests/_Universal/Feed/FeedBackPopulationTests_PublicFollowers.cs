using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
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
public class FeedBackPopulationTests_PublicFollowers
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
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
    public async Task FollowingIdentity_PopulatesFollowersFeedWithAnonymousFiles(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        const int fileType = 1038;

        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        var ownerSam = _scaffold.CreateOwnerApiClientRedux(sam);
        var ownerFrodo = _scaffold.CreateOwnerApiClientRedux(frodo);

        var friendsOnlyTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        var publicTargetDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        Guid friendsOnlyCircle = Guid.NewGuid();
        var samPreparedFiles = await PrepareSamIdentityWithChannelsAndPosts(friendsOnlyCircle, friendsOnlyTargetDrive,
            publicTargetDrive,
            postFileType: fileType);

        await callerContext.Initialize(ownerFrodo);

        //at this point we follow sam 
        var followerApiClient = new UniversalFollowerApiClient(TestIdentities.Frodo.OdinId, callerContext.GetFactory());
        var followSamResponse = await followerApiClient.FollowIdentity(TestIdentities.Samwise.OdinId,
            FollowerNotificationType.AllNotifications,
            new List<TargetDrive>() { });

        ClassicAssert.IsTrue(followSamResponse.IsSuccessStatusCode, $"actual status code was {followSamResponse.StatusCode}");

        //Crucial point - we have to tell frodo's identity sync to sam after we call follow
        // await ownerFrodo.Follower.SynchronizeFeed(ownerSam.Identity.OdinId);
        
        //
        // Validation - check that frodo has 4 files in his feed; files are from Sam, none are encrypted
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
                IncludeMetadataHeader = true
            }
        });

        ClassicAssert.IsTrue(frodoQueryFeedResponse.StatusCode == expectedStatusCode,
            $"Actual code was {frodoQueryFeedResponse.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults;
            ClassicAssert.IsNotNull(feedSearchResults);
            ClassicAssert.IsTrue(feedSearchResults.Count() == 4, $"actual count is {feedSearchResults.Count()}");

            var expectedFriendsOnlyFile = feedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted &&
                s.FileMetadata.AppData.Content == samPreparedFiles.encryptedFriendsFileContent64);
            ClassicAssert.IsNull(expectedFriendsOnlyFile, "there should be no friend's only files");

            var expectedPublicFile = feedSearchResults.SingleOrDefault(s =>
                s.FileMetadata.IsEncrypted == false &&
                s.FileMetadata.AppData.Content == samPreparedFiles.publicFileContent);
            ClassicAssert.IsNotNull(expectedPublicFile);
        }

        await ownerFrodo.Follower.UnfollowIdentity(sam.OdinId);
    }

    private async Task<(string encryptedFriendsFileContent64, string publicFileContent)>
        PrepareSamIdentityWithChannelsAndPosts(Guid circleId, TargetDrive friendsOnlyTargetDrive,
            TargetDrive publicTargetDrive, int postFileType)
    {

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        await samOwnerClient.DriveManager.CreateDrive(publicTargetDrive, "Public Channel Drive", "", allowAnonymousReads: true, false, allowSubscriptions: true);
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
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Anonymous);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult = await samOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);

        publicFile.AppData.Content = Guid.NewGuid().ToString();
        await samOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);

        publicFile.AppData.Content = Guid.NewGuid().ToString();
        await samOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);
        
        publicFile.AppData.Content = Guid.NewGuid().ToString();
        await samOwnerClient.DriveRedux.UploadNewMetadata(publicTargetDrive, publicFile);

        ClassicAssert.IsTrue(publicFileUploadResult.IsSuccessStatusCode);

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }
}