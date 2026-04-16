using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._Universal.ApiClient.Factory;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Controllers.Base.Drive;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Hosting.UnifiedV2.Drive.Write;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions;

namespace Odin.Hosting.Tests._V2.Tests.Drive.Reactions;

public class LocalReactionsTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: [TestIdentities.Pippin]);
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
    public async Task AddReactionPopulatesLocalReactions()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        const string reaction = ":thumbsup:";
        var addResponse = await reactionClient.AddReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(addResponse.IsSuccessStatusCode);

        var localReactions = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsNotNull(localReactions);
        ClassicAssert.IsTrue(localReactions.Contains(reaction), "LocalReactions should contain the added reaction");
    }

    [Test]
    public async Task DeleteReactionRemovesFromLocalReactions()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        const string reaction = ":heart:";
        var addResponse = await reactionClient.AddReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(addResponse.IsSuccessStatusCode);

        // Verify it was added
        var localReactionsAfterAdd = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localReactionsAfterAdd.Contains(reaction));

        // Delete it
        var deleteResponse = await reactionClient.DeleteReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);

        var localReactionsAfterDelete = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsFalse(localReactionsAfterDelete.Contains(reaction), "LocalReactions should not contain the deleted reaction");
    }

    [Test]
    public async Task ToggleReactionAddsToLocalReactions()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        const string reaction = ":fire:";
        var toggleResponse = await reactionClient.ToggleReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(toggleResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(toggleResponse.Content.ResultType == ToggleReactionResultType.Added);

        var localReactions = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localReactions.Contains(reaction), "LocalReactions should contain the toggled-on reaction");
    }

    [Test]
    public async Task ToggleReactionRemovesFromLocalReactions()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        const string reaction = ":star:";

        // Toggle on
        var toggle1 = await reactionClient.ToggleReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(toggle1.IsSuccessStatusCode);
        ClassicAssert.IsTrue(toggle1.Content.ResultType == ToggleReactionResultType.Added);

        // Toggle off
        var toggle2 = await reactionClient.ToggleReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(toggle2.IsSuccessStatusCode);
        ClassicAssert.IsTrue(toggle2.Content.ResultType == ToggleReactionResultType.Deleted);

        var localReactions = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsFalse(localReactions.Contains(reaction), "LocalReactions should not contain the toggled-off reaction");
    }

    [Test]
    public async Task AddMultipleReactionsPopulatesLocalReactions()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        var reactions = new[] { ":thumbsup:", ":heart:", ":fire:" };
        foreach (var reaction in reactions)
        {
            var addResponse = await reactionClient.AddReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
            ClassicAssert.IsTrue(addResponse.IsSuccessStatusCode);
        }

        var localReactions = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localReactions.Count == 3, $"Expected 3 local reactions but got {localReactions.Count}");
        foreach (var reaction in reactions)
        {
            ClassicAssert.IsTrue(localReactions.Contains(reaction), $"LocalReactions should contain {reaction}");
        }
    }

    [Test]
    public async Task AddDuplicateReactionDoesNotDuplicate()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        const string reaction = ":clap:";

        // Add twice - second add may fail at DB level (unique constraint) but LocalReactions should not duplicate
        await reactionClient.AddReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        await reactionClient.AddReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);

        var localReactions = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        var count = localReactions.Count(r => r == reaction);
        ClassicAssert.IsTrue(count == 1, $"LocalReactions should contain the reaction exactly once, but found {count}");
    }

    [Test]
    public async Task LocalReactionsSurvivesLocalMetadataContentUpdate()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        const string reaction = ":rocket:";
        var addResponse = await reactionClient.AddReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(addResponse.IsSuccessStatusCode);

        // Get the current local version tag
        var headerBeforeUpdate = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(headerBeforeUpdate.IsSuccessStatusCode);
        var localVersionTag = headerBeforeUpdate.Content.FileMetadata.LocalAppData?.VersionTag ?? Guid.Empty;

        // Update local metadata content
        var callerContext = new OwnerTestCase(targetDrive);
        await callerContext.Initialize(ownerApiClient);
        var writerClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var updateResponse = await writerClient.UpdateLocalAppMetadataContent(targetDrive.Alias, uploadResult.File.FileId,
            new UpdateLocalMetadataContentRequestV2
            {
                LocalVersionTag = localVersionTag,
                Content = "some updated content"
            });
        ClassicAssert.IsTrue(updateResponse.IsSuccessStatusCode);

        // Verify LocalReactions survived
        var localReactions = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localReactions.Contains(reaction),
            "LocalReactions should survive a local metadata content update");
    }

    [Test]
    public async Task LocalReactionsSurvivesLocalMetadataTagUpdate()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;

        var reactionClient = new DriveGroupReactionV2Client(identity.OdinId,
            CreateOwnerFactory(ownerApiClient));

        const string reaction = ":wave:";
        var addResponse = await reactionClient.AddReactionAsync(targetDrive.Alias, uploadResult.File.FileId, reaction);
        ClassicAssert.IsTrue(addResponse.IsSuccessStatusCode);

        // Get the current local version tag
        var headerBeforeUpdate = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(headerBeforeUpdate.IsSuccessStatusCode);
        var localVersionTag = headerBeforeUpdate.Content.FileMetadata.LocalAppData?.VersionTag ?? Guid.Empty;

        // Update local metadata tags
        var callerContext = new OwnerTestCase(targetDrive);
        await callerContext.Initialize(ownerApiClient);
        var writerClient = new DriveWriterV2Client(identity.OdinId, callerContext.GetFactory());
        var updateResponse = await writerClient.UpdateLocalAppMetadataTags(targetDrive.Alias, uploadResult.File.FileId,
            new UpdateLocalMetadataTagsRequestV2
            {
                LocalVersionTag = localVersionTag,
                Tags = [Guid.NewGuid(), Guid.NewGuid()]
            });
        ClassicAssert.IsTrue(updateResponse.IsSuccessStatusCode);

        // Verify LocalReactions survived
        var localReactions = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localReactions.Contains(reaction),
            "LocalReactions should survive a local metadata tag update");
    }

    [Test]
    public async Task StaleLocalReactionSelfCorrectsByDelete()
    {
        // Scenario: LocalReactions has a stale entry that no longer exists on the server.
        // Deleting it via group API should remove it from local even though the server has nothing to delete.
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var factory = CreateOwnerFactory(ownerApiClient);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;
        var driveId = targetDrive.Alias;
        var fileId = uploadResult.File.FileId;

        var groupClient = new DriveGroupReactionV2Client(identity.OdinId, factory);

        // Step 1: Add ":stale:" via group API (server + local both have it)
        const string staleReaction = ":stale:";
        var addResponse = await groupClient.AddReactionAsync(driveId, fileId, staleReaction);
        ClassicAssert.IsTrue(addResponse.IsSuccessStatusCode);

        var localAfterAdd = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localAfterAdd.Contains(staleReaction));

        // Step 2: Delete ":stale:" via V2 non-group (direct) endpoint — removes from server only, local keeps it
        await DeleteReactionDirectAsync(identity, factory, driveId, fileId, staleReaction);

        // Verify local still has it (stale now)
        var localAfterDirectDelete = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localAfterDirectDelete.Contains(staleReaction),
            "LocalReactions should still contain stale entry after direct (non-group) delete");

        // Step 3: Delete ":stale:" via group API — server no-ops, local removes it
        var deleteResponse = await groupClient.DeleteReactionAsync(driveId, fileId, staleReaction);
        ClassicAssert.IsTrue(deleteResponse.IsSuccessStatusCode);

        var localAfterGroupDelete = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsFalse(localAfterGroupDelete.Contains(staleReaction),
            "Stale entry should be self-corrected (removed) after group delete");
    }

    [Test]
    public async Task StaleLocalReactionSelfCorrectsByToggle()
    {
        // Scenario: LocalReactions has a stale entry that no longer exists on the server.
        // Toggling it via group API should re-add it on the server (toggle sees it's not there),
        // and local already has it — both end up in sync.
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var factory = CreateOwnerFactory(ownerApiClient);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", allowAnonymousReads: true);

        var uploadResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, SampleMetadataData.Create(fileType: 100));
        ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
        var uploadResult = uploadResponse.Content;
        var driveId = targetDrive.Alias;
        var fileId = uploadResult.File.FileId;

        var groupClient = new DriveGroupReactionV2Client(identity.OdinId, factory);

        // Step 1: Add ":stale:" via group API
        const string staleReaction = ":stale:";
        var addResponse = await groupClient.AddReactionAsync(driveId, fileId, staleReaction);
        ClassicAssert.IsTrue(addResponse.IsSuccessStatusCode);

        // Step 2: Delete from server only via direct endpoint
        await DeleteReactionDirectAsync(identity, factory, driveId, fileId, staleReaction);

        // Local still has it, server doesn't — local is stale
        var localAfterDirectDelete = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localAfterDirectDelete.Contains(staleReaction));

        // Step 3: Toggle ":stale:" — server doesn't have it, so toggle ADDS it back
        var toggleResponse = await groupClient.ToggleReactionAsync(driveId, fileId, staleReaction);
        ClassicAssert.IsTrue(toggleResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(toggleResponse.Content.ResultType == ToggleReactionResultType.Added,
            "Toggle should add the reaction back since server doesn't have it");

        // Both server and local now have it — in sync
        var localAfterToggle = await GetLocalReactionsAsync(ownerApiClient, uploadResult.File);
        ClassicAssert.IsTrue(localAfterToggle.Contains(staleReaction));

        // Verify server has it too by checking via group reaction count
        var countsResponse = await groupClient.GetReactionCountsByFileAsync(driveId, fileId);
        ClassicAssert.IsTrue(countsResponse.IsSuccessStatusCode);
        var hasOnServer = countsResponse.Content.Reactions.Any(r => r.ReactionContent == staleReaction);
        ClassicAssert.IsTrue(hasOnServer, "Server should have the reaction after toggle re-added it");
    }

    private async Task DeleteReactionDirectAsync(TestIdentity identity, IApiClientFactory factory,
        Guid driveId, Guid fileId, string reaction)
    {
        var client = factory.CreateHttpClient(identity.OdinId, out var sharedSecret);
        var svc = RefitCreator.RestServiceFor<IDriveDirectReactionHttpClientApiV2>(client, sharedSecret);
        var response = await svc.DeleteReaction(driveId, fileId, new DeleteReactionRequest { Reaction = reaction });
        ClassicAssert.IsTrue(response.IsSuccessStatusCode,
            $"Direct delete should succeed, got {response.StatusCode}");
    }

    private async Task<List<string>> GetLocalReactionsAsync(OwnerApiClientRedux ownerApiClient, ExternalFileIdentifier file)
    {
        var headerResponse = await ownerApiClient.DriveRedux.GetFileHeader(file);
        ClassicAssert.IsTrue(headerResponse.IsSuccessStatusCode);
        return headerResponse.Content.FileMetadata.LocalAppData?.LocalReactions ?? new List<string>();
    }

    private IApiClientFactory CreateOwnerFactory(OwnerApiClientRedux ownerApiClient)
    {
        var t = ownerApiClient.GetTokenContext();
        return new _V2.ApiClient.Factory.ApiClientFactoryV2(
            Odin.Services.Authentication.Owner.OwnerAuthConstants.CookieName,
            t.AuthenticationResult,
            t.SharedSecret.GetKey());
    }
}
