using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using JetBrains.FormatRipper.FileExplorer;
using NUnit.Framework;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Drives.DriveCore.Query;
using Odin.Core.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.SendingHost;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;

namespace Odin.Hosting.Tests._Universal.Feed;

//Tests to validate we can distribute older feed items to
//recipients (i.e. in the case of when we're first connected, I want my feed items to show in your feed)
public class FeedBackPopulationTests
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
        // yield return new object[] { new AppReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
        yield return new object[] { new OwnerClientContext(SystemDriveConstants.FeedDrive), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task TransitSendsAppNotification(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // what is the primary thing being tested here? - frodo's feed has 2 posts from sam, one secured, one public

        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        // Frodo sends connection request to Sam, Sam approves and puts Frodo in the friends circle
        // Frodo follows Sam

        // Upon following Sam, frodo requests back population

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


        // grant frodo access to friends only
        await ownerSam.Connections.SendConnectionRequest(frodo.OdinId, new List<GuidId>() { friendsOnlyCircle });
        await ownerFrodo.Connections.AcceptConnectionRequest(sam.OdinId, new List<GuidId>() { });

        await callerContext.Initialize(ownerFrodo);

        // TODO: frodo to follow sam - here we flag to back populate the feed?

        //
        // Validation - check that frodo has 2 files in his feed; files are from Sam, one encrypted, one is not encrypted
        //
        // var driveClient = new UniversalDriveApiClient(TestIdentities.Frodo.OdinId, callerContext.GetFactory());
        // var frodoQueryFeedResponse = await driveClient.QueryBatch(new QueryBatchRequest()
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

        Assert.IsTrue(frodoQueryFeedResponse.StatusCode == expectedStatusCode, $"Actual code was {frodoQueryFeedResponse.StatusCode}");

        if(expectedStatusCode == HttpStatusCode.OK) //continue testing
        {
            var feedSearchResults = frodoQueryFeedResponse.Content?.SearchResults;
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
        
        await ownerFrodo.Connections.DisconnectFrom(sam.OdinId);
        await ownerSam.Connections.DisconnectFrom(frodo.OdinId);
    }

    private async Task<(string encryptedFriendsFileContent64, string publicFileContent)>
        PrepareSamIdentityWithChannelsAndPosts(Guid circleId, TargetDrive friendsOnlyTargetDrive,
            TargetDrive publicTargetDrive, int postFileType)
    {
        // Sam's identity creates the circle 'friends' with read access to a channel drive.
        // Sam's posts 1 item to this friends channel drive
        // sam posts 1 item to a public channel drive

        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        await samOwnerClient.DriveManager.CreateDrive(publicTargetDrive, "Public Channel Drive", "", false);
        await samOwnerClient.DriveManager.CreateDrive(friendsOnlyTargetDrive, "Secured Channel Drive", "", false);

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
        var friendsFile =
            SampleMetadataData.CreateWithContent(postFileType, friendsOnlyContent, AccessControlList.Connected);
        friendsFile.AllowDistribution = true;
        var friendsFileUploadResponse = await samOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
            friendsOnlyTargetDrive,
            friendsFile,
            useGlobalTransitId: true);

        Assert.IsTrue(friendsFileUploadResponse.response.IsSuccessStatusCode);

        //
        // upload one post to public target drive
        //
        const string publicContent = "some public content";
        var publicFile = SampleMetadataData.CreateWithContent(postFileType, publicContent, AccessControlList.Connected);
        publicFile.AllowDistribution = true;
        var publicFileUploadResult =
            await samOwnerClient.DriveRedux.UploadNewMetadata(friendsOnlyTargetDrive, publicFile,
                useGlobalTransitId: true);

        Assert.IsTrue(publicFileUploadResult.IsSuccessStatusCode);

        return (friendsFileUploadResponse.encryptedJsonContent64, publicContent);
    }
}