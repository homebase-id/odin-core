using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.ReactionsApi;

#nullable enable

public class ReactionsIntegrationTests
{
    private WebScaffold _scaffold = null!;

    [SetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;    
        _scaffold = new WebScaffold(folder);                                                                    
        _scaffold.RunBeforeAnyTests();
    }

    [TearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    #region list - get all reactions

    //

    [Test]
    public async Task PublicPost_CanGetEmptyReactionList_As_OwnerUser_DirectCall()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(frodo);

        // Act
        var response = await ownerApiClient.Reactions2.GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);

        // Assert
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content!.Reactions;
        Assert.IsEmpty(reactions);
    }

    //

    [Test]
    public async Task PublicPost_CanGetEmptyReactionList_As_AuthenticatedUser_DirectCall()
    {
        // Arrange
        await ConnectHobbits();

        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");

        var sam = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(sam);

        var tokenContext = ownerApiClient.GetTokenContext();

        var reactionClient = new UniversalDriveReactionClient2(
            frodo.OdinId,
            new GuestApiClientFactory(tokenContext.AuthenticationResult, tokenContext.SharedSecret.GetKey()));

        var response = await reactionClient.GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);

        // Assert
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content!.Reactions;
        Assert.IsEmpty(reactions);
    }

    //

    [Test]
    public async Task PublicPost_CanGetEmptyReactionList_As_AnonymousUser_DirectCall()
    {
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");

        var reactionClient = new UniversalDriveReactionClient2(frodo.OdinId, new GuestApiClientFactory());
        var response = await reactionClient.GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);

        // Assert
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content!.Reactions;
        Assert.IsEmpty(reactions);
    }

    #endregion

    //

    [Test]
    public async Task PublicPost_CanCreateReactionsAndDeleteThemAgain_As_OwnerUser_DirectCall()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(frodo);

        // Act - create reactions
        var r1 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        var r2 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like2");
        Assert.AreEqual(r2.StatusCode, HttpStatusCode.NoContent);
        var r3 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
        Assert.AreEqual(r3.StatusCode, HttpStatusCode.NoContent);

        // Act - Already exists
        var r4 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r4.StatusCode, HttpStatusCode.NoContent);

        // Assert get
        var reactions = await GetReactions(ownerApiClient.Reactions2, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(3));
        Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.True);
        Assert.That(reactions.Exists(x => x.ReactionContent == "like2"), Is.True);
        Assert.That(reactions.Exists(x => x.ReactionContent == "like3"), Is.True);

        // Delete "like1"
        r1 = await ownerApiClient.Reactions2.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(ownerApiClient.Reactions2, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(2));
        Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.False);

        // Delete the test
        r1 = await ownerApiClient.Reactions2.DeleteAllReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(ownerApiClient.Reactions2, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(0));

        // No-op
        r1 = await ownerApiClient.Reactions2.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(ownerApiClient.Reactions2, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(0));
    }

    //
    
    
    [Test]
    // [TestCase("frodo.dotyou.cloud")] // frodo is owner
    // [TestCase("sam.dotyou.cloud")]   // sam is guest
    public async Task PublicPost_CanCreateReactionsAndDeleteThemAgain_As_AppUser()
    {
        // TODD:HELP!
        // - frodo and sam are connected
        // - sam follows frodo
        // - frodo creates a public post
        // - sam creates a (feed) app 
        // - sam adds reactions to the post using the app
        // ABOVE throws "I'm throwing because it's false!" at frodo's side because inside CallerHasPermission,
        //   the ACL on the drive says RequiredSecurityGroup == Owner
        
        // Arrange
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;

        await ConnectFrodoAndSam(samFollowsFrodo: true);
        
        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");

        // setup the feed app and grant access to the feed
        var samOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(sam);        
        var samAppFactory = await PrepareAnApp(samOwnerApiClient);
        var samAppClient = new UniversalDriveReactionClient2(sam.OdinId, samAppFactory);

        // Act - create reactions
        var r1 = await samAppClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        var r2 = await samAppClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like2");
        Assert.AreEqual(r2.StatusCode, HttpStatusCode.NoContent);
        var r3 = await samAppClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
        Assert.AreEqual(r3.StatusCode, HttpStatusCode.NoContent);

        // Act - Already exists
        var r4 = await samAppClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r4.StatusCode, HttpStatusCode.NoContent);

        // Assert get
        var reactions = await GetReactions(samAppClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(3));
        Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.True);
        Assert.That(reactions.Exists(x => x.ReactionContent == "like2"), Is.True);
        Assert.That(reactions.Exists(x => x.ReactionContent == "like3"), Is.True);

        // Delete "like1"
        r1 = await samAppClient.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(samAppClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(2));
        Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.False);

        // Delete the test
        r1 = await samAppClient.DeleteAllReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(samAppClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(0));

        // No-op
        r1 = await samAppClient.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(samAppClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(0));
    }
    
    // private async Task CreateReactionsThenDeleteThem(
    //     UniversalDriveReactionClient2 apiClient,
    //     TestIdentity ownerOfPost, 
    //     UploadResult post)
    // {
    //     // Arrange
    //     var appUser = TestIdentities.All[appUserOdindId];
    //     var frodo = TestIdentities.Frodo;
    //     var postFile = await CreatePublicPost(frodo, "hello world");
    //
    //     // setup the feed app and grant access to the feed
    //     var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(appUser);
    //     var appFactory = await PrepareAnApp(ownerApiClient, postFile.File.TargetDrive);
    //     var appClient = new UniversalDriveReactionClient2(appUser.OdinId, appFactory);
    //
    //     // Act - create reactions
    //     var r1 = await appClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
    //     Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
    //     var r2 = await appClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like2");
    //     Assert.AreEqual(r2.StatusCode, HttpStatusCode.NoContent);
    //     var r3 = await appClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
    //     Assert.AreEqual(r3.StatusCode, HttpStatusCode.NoContent);
    //
    //     // Act - Already exists
    //     var r4 = await appClient.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
    //     Assert.AreEqual(r4.StatusCode, HttpStatusCode.NoContent);
    //
    //     // Assert get
    //     var reactions = await GetReactions(appClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
    //     Assert.That(reactions.Count, Is.EqualTo(3));
    //     Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.True);
    //     Assert.That(reactions.Exists(x => x.ReactionContent == "like2"), Is.True);
    //     Assert.That(reactions.Exists(x => x.ReactionContent == "like3"), Is.True);
    //
    //     // Delete "like1"
    //     r1 = await appClient.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
    //     Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
    //     reactions = await GetReactions(appClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
    //     Assert.That(reactions.Count, Is.EqualTo(2));
    //     Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.False);
    //
    //     // Delete the test
    //     r1 = await appClient.DeleteAllReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
    //     Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
    //     reactions = await GetReactions(appClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
    //     Assert.That(reactions.Count, Is.EqualTo(0));
    //
    //     // No-op
    //     r1 = await appClient.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
    //     Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
    //     reactions = await GetReactions(appClient, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
    //     Assert.That(reactions.Count, Is.EqualTo(0));
    // }    
    
    //
    
    [Test]
    public async Task PublicPost_CanGetReactionsSummary_As_OwnerUser_DirectCall()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(frodo);

        // Act - create reactions
        var r1 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        var r2 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like2");
        Assert.AreEqual(r2.StatusCode, HttpStatusCode.NoContent);
        var r3 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
        Assert.AreEqual(r3.StatusCode, HttpStatusCode.NoContent);
        var r4 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3"); // no-op
        Assert.AreEqual(r4.StatusCode, HttpStatusCode.NoContent);

        var reactions = await GetReactions(ownerApiClient.Reactions2, frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(3));

        var summary = await ownerApiClient.Reactions2.GetReactionCountsByFile(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.AreEqual(summary.StatusCode, HttpStatusCode.OK);
        Assert.That(summary.Content!.Reactions.Count, Is.EqualTo(3));
        Assert.That(summary.Content.Reactions.Find(x => x.ReactionContent.Equals("like1"))!.Count, Is.EqualTo(1));
        Assert.That(summary.Content.Reactions.Find(x => x.ReactionContent.Equals("like2"))!.Count, Is.EqualTo(1));
        Assert.That(summary.Content.Reactions.Find(x => x.ReactionContent.Equals("like3"))!.Count, Is.EqualTo(1));
    }

    //

    [Test]
    public async Task PublicPost_CanGetReactionsByIdentity_As_OwnerUser_DirectCall()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(frodo);

        // Act - create reactions
        var r1 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        var r2 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like2");
        Assert.AreEqual(r2.StatusCode, HttpStatusCode.NoContent);
        var r3 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
        Assert.AreEqual(r3.StatusCode, HttpStatusCode.NoContent);
        var r4 = await ownerApiClient.Reactions2.AddReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3"); // no-op
        Assert.AreEqual(r4.StatusCode, HttpStatusCode.NoContent);

        var reactions = await ownerApiClient.Reactions2.GetReactionsByIdentity(frodo, frodo.OdinId, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Content!.Count, Is.EqualTo(3));
        Assert.That(reactions.Content.Exists(x => x == "like1"), Is.True);
        Assert.That(reactions.Content.Exists(x => x == "like2"), Is.True);
        Assert.That(reactions.Content.Exists(x => x == "like3"), Is.True);
    }


    [Test]
    public async Task PublicPost_CanGetReactionsByIdentity_As_AuthenticatedUser()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var frodoOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(frodo);
        var samOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(sam);

        // frodo and sam connect
        // await frodoOwnerApiClient.Connections.SendConnectionRequest(sam.OdinId, []);
        // await samOwnerApiClient.Connections.AcceptConnectionRequest(frodo.OdinId, []);

        // sam follows frodo
        await samOwnerApiClient.Follower.FollowIdentity(frodo.OdinId, FollowerNotificationType.AllNotifications);

        var postFile = await CreatePublicPostAndDistribute(frodo, "hello world");

        // wait for post to be distributed to sam
        await frodoOwnerApiClient.DriveRedux.WaitForEmptyOutbox(postFile.File.TargetDrive);

        //Prepare an app on sam's identity
        var samAppFactory = await PrepareAnApp(samOwnerApiClient);
        var samAppClient = new UniversalDriveReactionClient2(sam.OdinId, samAppFactory);
        var response = await samAppClient.GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);

        // Assert
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content!.Reactions;
        Assert.IsEmpty(reactions);
    }
    //



    #region helpers
    
    //

    private async Task ConnectHobbits()
    {
        var targetDrive = TargetDrive.NewTargetDrive();
        await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);
    }

    //
    
    private async Task ConnectFrodoAndSam(bool frodoFollowsSam = false, bool samFollowsFrodo = false)
    {
        var frodo = TestIdentities.Frodo;
        var sam = TestIdentities.Samwise;
        var frodoOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(frodo);
        var samOwnerApiClient = _scaffold.CreateOwnerApiClientRedux(sam);
        
        await frodoOwnerApiClient.Connections.SendConnectionRequest(sam.OdinId, []);
        await samOwnerApiClient.Connections.AcceptConnectionRequest(frodo.OdinId, []);

        if (frodoFollowsSam)
        {
            await frodoOwnerApiClient.Follower.FollowIdentity(sam.OdinId, FollowerNotificationType.AllNotifications);    
        }
        
        if (samFollowsFrodo)
        {
            await samOwnerApiClient.Follower.FollowIdentity(frodo.OdinId, FollowerNotificationType.AllNotifications);    
        }
    }
    
    //

    private async Task<UploadResult> CreatePublicPostAndDistribute(TestIdentity identity, string postContent)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = SystemDriveConstants.PublicPostsChannelDrive;

        // Upload post
        var uploadedFileMetadata = SampleMetadataData.CreateWithContent(fileType: 100, postContent, AccessControlList.Anonymous);
        uploadedFileMetadata.AllowDistribution = true;
        
        var uploadPostContentResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(uploadPostContentResponse.IsSuccessStatusCode);
        var uploadResult = uploadPostContentResponse.Content;

        // Read the post back
        var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult!.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);

        // Verify the post content
        var ss = getHeaderResponse.Content;
        Assert.IsTrue(ss?.FileMetadata.AppData.Content == postContent);

        // wait for distributions (if any)
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(uploadResult.File.TargetDrive);
        
        return uploadResult;
    }

    //

    private async Task<List<Reaction>> GetReactions(
        UniversalDriveReactionClient2 client,
        TestIdentity identity,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier)
    {
        var response = await client.GetReactions(identity, file, globalTransitIdFileIdentifier);
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content!.Reactions;
        return reactions;
    }
    
    //
    
    private async Task<AppApiClientFactory> PrepareAnApp(OwnerApiClientRedux ownerClient, TargetDrive? targetDrive = null)
    {
        // Prepare the app
        var appId = Guid.NewGuid();
        
        var permissions = new PermissionSetGrantRequest()
        {
            Drives = targetDrive == null 
                ? [] 
                : new List<DriveGrantRequest>
                    {
                        new()
                        {
                            PermissionedDrive = new PermissionedDrive()
                            {
                                Drive = targetDrive,
                                Permission = DrivePermission.React
                            }
                        }
                    },
            PermissionSet = new PermissionSet(PermissionKeys.UseTransitRead)
        };

        var circles = new List<Guid>();
        var circlePermissions = new PermissionSetGrantRequest();
        await ownerClient.AppManager.RegisterApp(appId, permissions, circles, circlePermissions);

        var (appToken, appSharedSecret) = await ownerClient.AppManager.RegisterAppClient(appId);
        return new AppApiClientFactory(appToken, appSharedSecret);
    }

    //

    #endregion
}