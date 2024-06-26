using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Hosting.Controllers.Reactions.DTOs;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query.Sqlite;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.ReactionsApi;

public class ReactionsIntegrationTests
{
    private WebScaffold _scaffold;

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
    public async Task Owner_PublicPost_CanGetEmptyReactionList()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPost(frodo, "hello world");
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(frodo);

        // Act
        var response = await ownerApiClient.Reactions2.GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);

        // Assert
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content.Reactions;
        Assert.IsEmpty(reactions);
    }

    //

    [Test]
    public async Task Connection_PublicPost_CanGetEmptyReactionList()
    {
        // Arrange
        await ConnectHobbits();

        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPost(frodo, "hello world");

        var sam = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(sam);

        // Act
        var response = await ownerApiClient.Reactions2.GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);

        // Assert
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content.Reactions;
        Assert.IsEmpty(reactions);
    }

    //

    [Test]
    public async Task Anonymous_PublicPost_CanGetEmptyReactionList()
    {
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPost(frodo, "hello world");

        var url = "https://frodo.dotyou.cloud:8443/api/guest/v1/unified-reactions/list";

        var request = new GetReactionsRequest2
        {
            AuthorOdinId = frodo.OdinId,
            TargetDrive = postFile.File.TargetDrive,
            FileId = postFile.File.FileId,
            GlobalTransitId = postFile.GlobalTransitIdFileIdentifier.GlobalTransitId,
            Cursor = 0,
            MaxRecords = int.MaxValue
        };

        var json = OdinSystemSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = new HttpClient();
        var r3 = await client.PostAsync(url, content);
        var c = await r3.Content.ReadAsStringAsync();

        var t = _scaffold.OldOwnerApi.GetOwnerAuthContext(frodo.OdinId).GetAwaiter().GetResult();
        var factory = new OwnerApiClientFactory(t.AuthenticationResult, t.SharedSecret.GetKey());
        var reactionClient = new UniversalDriveReactionClient2(frodo.OdinId, new GuestApiClientFactory());
        
        // var reactionClient = new UniversalDriveReactionClient2(frodo.OdinId, new GuestApiClientFactory());

        var response = await reactionClient.GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);

        // Assert
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content.Reactions;
        Assert.IsEmpty(reactions);
    }

    #endregion


    //

    [Test]
    public async Task Owner_PublicPost_CanCreateReactionsAndDeleteThemAgain()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPost(frodo, "hello world");
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
        var reactions = await GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(3));
        Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.True);
        Assert.That(reactions.Exists(x => x.ReactionContent == "like2"), Is.True);
        Assert.That(reactions.Exists(x => x.ReactionContent == "like3"), Is.True);

        // Delete "like1"
        r1 = await ownerApiClient.Reactions2.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like1");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(2));
        Assert.That(reactions.Exists(x => x.ReactionContent == "like1"), Is.False);

        // Delete the test
        r1 = await ownerApiClient.Reactions2.DeleteAllReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(0));

        // No-op
        r1 = await ownerApiClient.Reactions2.DeleteReaction(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier, "like3");
        Assert.AreEqual(r1.StatusCode, HttpStatusCode.NoContent);
        reactions = await GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(0));
    }

    //

    [Test]
    public async Task Owner_PublicPost_CanGetReactionsSummary()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPost(frodo, "hello world");
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

        var reactions = await GetReactions(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.That(reactions.Count, Is.EqualTo(3));

        var summary = await ownerApiClient.Reactions2.GetReactionCountsByFile(frodo, postFile.File, postFile.GlobalTransitIdFileIdentifier);
        Assert.AreEqual(summary.StatusCode, HttpStatusCode.OK);
        Assert.That(summary.Content!.Reactions.Count, Is.EqualTo(3));
        Assert.That(summary.Content.Reactions.Find(x => x.ReactionContent.Equals("like1")).Count, Is.EqualTo(1));
        Assert.That(summary.Content.Reactions.Find(x => x.ReactionContent.Equals("like2")).Count, Is.EqualTo(1));
        Assert.That(summary.Content.Reactions.Find(x => x.ReactionContent.Equals("like3")).Count, Is.EqualTo(1));
    }

    //

    [Test]
    public async Task Owner_PublicPost_CanGetReactionsByIdentity()
    {
        // Arrange
        var frodo = TestIdentities.Frodo;
        var postFile = await CreatePublicPost(frodo, "hello world");
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



    #region helpers

    //

    private async Task ConnectHobbits()
    {
        var targetDrive = TargetDrive.NewTargetDrive();
        await _scaffold.Scenarios.CreateConnectedHobbits(targetDrive);
    }

    //

    private async Task<UploadResult> CreatePublicPost(TestIdentity identity, string postContent)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = SystemDriveConstants.PublicPostsChannelDrive;

        // Upload post
        var uploadedFileMetadata = SampleMetadataData.CreateWithContent(fileType: 100, postContent);
        var uploadPostContentResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        Assert.IsTrue(uploadPostContentResponse.IsSuccessStatusCode);
        var uploadResult = uploadPostContentResponse.Content;

        // Read the post back
        var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);

        // Verify the post content
        var ss = getHeaderResponse.Content;
        Assert.IsTrue(ss.FileMetadata.AppData.Content == postContent);

        return uploadResult;
    }

    //

    private async Task<List<Reaction>> GetReactions(
        TestIdentity identity,
        ExternalFileIdentifier file,
        GlobalTransitIdFileIdentifier globalTransitIdFileIdentifier)
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var response = await ownerApiClient.Reactions2.GetReactions(identity, file, globalTransitIdFileIdentifier);
        Assert.AreEqual(response.StatusCode, HttpStatusCode.OK);
        var reactions = response.Content.Reactions;
        return reactions;
    }

    #endregion
}