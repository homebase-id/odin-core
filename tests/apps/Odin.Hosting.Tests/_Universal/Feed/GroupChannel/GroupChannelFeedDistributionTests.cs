using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests._Universal.Feed.GroupChannel;

/// <summary>
/// Tests that guests (via youauth) can post to a group channel and those posts
/// are distributed to all connected identities which follow the channel
/// </summary>
public class GroupChannelFeedDistributionTests
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


    [Test]
    [Ignore("need to fix guest access bug")]
    public async Task FeedDistributionSucceedsWhenGuestPostsToGroupChannel()
    {
        // Group channel is hosted by frodo
        // Create a channel drive with attribute IsGroupChannel=true
        // Sam and pippin are guests that follow the channel
        // Pippin posts content
        // the content is in both Sams and Frodo's feed drives

        var groupIdentity = TestIdentities.Frodo;
        var (channelDrive, groupCircleId) = await CreateGroupChannel(groupIdentity);

        await SetupConnection(groupIdentity, TestIdentities.Samwise, groupCircleId);
        await SetupConnection(groupIdentity, TestIdentities.Pippin, groupCircleId);

        var (uploadResult, encryptedContent64) = await PostContent(author: TestIdentities.Pippin,
            groupIdentity: groupIdentity,
            targetDrive: channelDrive,
            groupCircleId);

        //
        // Feed distribution should happen here
        //
        // await _scaffold.CreateOwnerApiClientRedux(groupIdentity).DriveRedux.WaitForEmptyOutbox(channelDrive);

        //
        // Validation - check that sam has the file in his feed
        //
        var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var queryFeedResponse = await samOwnerClient.DriveRedux.QueryBatch(new QueryBatchRequest()
        {
            QueryParams = new FileQueryParams()
            {
                TargetDrive = SystemDriveConstants.FeedDrive,
                GlobalTransitId = [uploadResult.GlobalTransitId.GetValueOrDefault()]
            },
            ResultOptionsRequest = new QueryBatchResultOptionsRequest()
            {
                MaxRecords = 10,
                IncludeMetadataHeader = true
            }
        });

        ClassicAssert.IsTrue(queryFeedResponse.IsSuccessStatusCode, $"Actual code was {queryFeedResponse.StatusCode}");

        var thePost = queryFeedResponse.Content?.SearchResults.Single();
        ClassicAssert.IsNotNull(thePost);
        ClassicAssert.IsTrue(thePost.FileMetadata.IsEncrypted);
        ClassicAssert.IsTrue(thePost.FileMetadata.AppData.Content == encryptedContent64);

        await Disconnect(groupIdentity, TestIdentities.Samwise);
        await Disconnect(groupIdentity, TestIdentities.Pippin);
    }

    private async Task SetupConnection(TestIdentity groupIdentity, TestIdentity identity, Guid groupCircleId)
    {
        var groupOwnerClient = _scaffold.CreateOwnerApiClientRedux(groupIdentity);
        var identityOwnerClient = _scaffold.CreateOwnerApiClientRedux(identity);

        var sendConnectionResponse = await identityOwnerClient.Connections.SendConnectionRequest(groupIdentity.OdinId);
        ClassicAssert.IsTrue(sendConnectionResponse.IsSuccessStatusCode);

        var acceptConnectionResponse = await groupOwnerClient.Connections.AcceptConnectionRequest(identity.OdinId, [groupCircleId]);
        ClassicAssert.IsTrue(acceptConnectionResponse.IsSuccessStatusCode);

        var followResponse = await identityOwnerClient.Follower.FollowIdentity(groupIdentity.OdinId, FollowerNotificationType.AllNotifications);
        ClassicAssert.IsTrue(followResponse.IsSuccessStatusCode);
    }

    private async Task<(TargetDrive channelDrive, Guid groupCircleId)> CreateGroupChannel(TestIdentity groupIdentity)
    {
        var groupOwnerClient = _scaffold.CreateOwnerApiClientRedux(groupIdentity);

        var channelDrive = new TargetDrive()
        {
            Alias = Guid.Parse("77777777-a226-4baa-b650-63cdb1cda924"),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await groupOwnerClient.DriveManager.CreateDrive(channelDrive, "a group channel", "", false, false, true);

        var groupCircleId = Guid.Parse("55555555-a226-4baa-b650-63cdb1cda924");
        var grant = new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = channelDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        };

        await groupOwnerClient.Network.CreateCircle(groupCircleId, "a group circle", grant);

        return (channelDrive, groupCircleId);
    }

    private async Task<(UploadResult uploadResult, string encryptedJsonContent64)> PostContent(TestIdentity author, TestIdentity groupIdentity,
        TargetDrive targetDrive, Guid groupCircleId)
    {
        //
        // first create an API client to use the guest api
        //
        var driveGrants = new List<DriveGrantRequest>()
        {
            new()
            {
                PermissionedDrive = new PermissionedDrive()
                {
                    Drive = targetDrive,
                    Permission = DrivePermission.ReadWrite
                }
            }
        };

        var keys = new TestPermissionKeyList([PermissionKeys.SendOnBehalfOfOwner]);
        var guestAccess = new GuestAccess(author.OdinId, driveGrants, circles: [], keys);
        await guestAccess.Initialize(_scaffold.CreateOwnerApiClientRedux(groupIdentity));
        var guestDriveClient = new UniversalDriveApiClient(groupIdentity.OdinId, guestAccess.GetFactory());

        //
        // Post a file as guest
        //
        const int fileType = 1039;
        const string content = "some secured friends only content";
        var file = SampleMetadataData.CreateWithContent(fileType, content, AccessControlList.Connected);
        file.AllowDistribution = true;

        var uploadResponse = await guestDriveClient.UploadNewEncryptedMetadata(
            targetDrive,
            file);

        ClassicAssert.IsTrue(uploadResponse.response.IsSuccessStatusCode);

        return (uploadResponse.response.Content, uploadResponse.encryptedJsonContent64);
    }

    private async Task Disconnect(TestIdentity groupIdentity, TestIdentity identity)
    {
        var groupOwnerClient = _scaffold.CreateOwnerApiClientRedux(groupIdentity);
        var identityOwnerClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await identityOwnerClient.Follower.UnfollowIdentity(groupIdentity.OdinId);
        await groupOwnerClient.Connections.DisconnectFrom(identity.OdinId);
    }
}