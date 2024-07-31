using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Drive.ReactionsRedux;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Reactions.Group;

namespace Odin.Hosting.Tests._Universal.DriveTests.Reactions;

/// <summary>
/// Tests reactions being distributed to other identities
/// </summary>
public class ReactionTestsDistributeToOthers
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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppAllowed()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanAddAndDistributeReaction(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.IsNotNull(uploadResult);

        List<TestIdentity> recipients = [TestIdentities.Merry, TestIdentities.Samwise];

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        const string reactionContent1 = ":cake:";
        var response = await callerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions
            {
                Recipients = recipients.ToStringList()
            }
        });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            // Validate the reaction is there locally
            var getHeaderResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
            Assert.IsNotNull(getHeaderResponse1.Content.FileMetadata.ReactionPreview.Reactions
                .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1));

            var getReactionsResponse = await ownerApiClient.Reactions.GetAllReactions(uploadResult.File.ToFileIdentifier());
            var hasReactionInDb = getReactionsResponse.Content.Reactions.All(r => r.ReactionContent != reactionContent1);
            Assert.IsTrue(hasReactionInDb);

            var globalTransitFileId = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier();
            foreach (var recipient in recipients)
            {
                await AssertIdentityHasReaction(recipient, globalTransitFileId, reactionContent1);
            }
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanDistributeDeleteReaction(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        Assert.IsNotNull(uploadResult);

        const string reactionContent1 = ":pie:";

        List<TestIdentity> recipients = [TestIdentities.Merry, TestIdentities.Samwise];

        var addReactionResponse = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions()
            {
                Recipients = recipients.ToStringList()
            }
        });

        Assert.IsTrue(addReactionResponse.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = new ReactionTransitOptions()
            {
                Recipients = recipients.ToStringList()
            }
        });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            await AssertIdentityHasReactionInPreview(identity, uploadResult.File.ToFileIdentifier(), reactionContent1);
            await AssertIdentityDoesNotHaveReaction(identity, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);

            foreach (var recipient in recipients)
            {
                await AssertIdentityDoesNotHaveReactionInPreview(recipient, uploadResult.File.ToFileIdentifier(), reactionContent1);
                await AssertIdentityDoesNotHaveReaction(recipient, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
            }
        }
    }
    
    private async Task AssertIdentityDoesNotHaveReactionInPreview(TestIdentity identity, FileIdentifier fileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getHeaderResponse1 = await client.DriveRedux.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier
        {
            TargetDrive = fileId.TargetDrive,
            GlobalTransitId = fileId.GlobalTransitId.GetValueOrDefault()
        });

        var file = getHeaderResponse1.Content.SearchResults.First();
        var hasReactionInPreview = file.FileMetadata.ReactionPreview.Reactions.All(pair => pair.Value.ReactionContent != reactionContent);
        Assert.IsFalse(hasReactionInPreview);
    }

    private async Task AssertIdentityHasReactionInPreview(TestIdentity identity, FileIdentifier fileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getHeaderResponse1 = await client.DriveRedux.QueryByGlobalTransitId(new GlobalTransitIdFileIdentifier
        {
            TargetDrive = fileId.TargetDrive,
            GlobalTransitId = fileId.GlobalTransitId.GetValueOrDefault()
        });

        var file = getHeaderResponse1.Content.SearchResults.First();
        var hasReactionInPreview = file.FileMetadata.ReactionPreview.Reactions.All(pair => pair.Value.ReactionContent != reactionContent);
        Assert.IsTrue(hasReactionInPreview);
    }

    private async Task AssertIdentityHasReaction(TestIdentity identity, FileIdentifier globalTransitFileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getReactionsResponse = await client.Reactions.GetAllReactions(globalTransitFileId);
        var hasReactionInDb =
            getReactionsResponse.Content.Reactions.All(r => r.ReactionContent != reactionContent);
        Assert.IsTrue(hasReactionInDb);
    }

    private async Task AssertIdentityDoesNotHaveReaction(TestIdentity identity, FileIdentifier globalTransitFileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getReactionsResponse = await client.Reactions.GetAllReactions(globalTransitFileId);
        var hasReactionInDb =
            getReactionsResponse.Content.Reactions.All(r => r.ReactionContent != reactionContent);
        Assert.IsFalse(hasReactionInDb);
    }
}