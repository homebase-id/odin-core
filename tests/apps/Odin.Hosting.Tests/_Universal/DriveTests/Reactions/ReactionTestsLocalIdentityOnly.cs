using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.DriveTests.Reactions;

/// <summary>
/// Tests reactions on the local identity (i.e. nothing going over transit)
/// </summary>
public class ReactionTestsLocalIdentityOnly
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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppAllowedReactOnly()
    {
        yield return new object[] { new AppSpecifyDriveAccess(TargetDrive.NewTargetDrive(), DrivePermission.React), HttpStatusCode.OK };
    }

    public static IEnumerable GuestMethodNotAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestMethodNotAllowed))]
    public async Task CanAddReaction(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        const string reactionContent1 = ":k:";
        var response = await callerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            await AssertIdentityHasReactionInPreview(identity, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
            await AssertIdentityHasReaction(identity, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestMethodNotAllowed))]
    public async Task CanDeleteReaction(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var localIdentity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(localIdentity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        const string reactionContent1 = ":k:";

        var addReactionResponse = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(addReactionResponse.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(localIdentity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            await AssertIdentityDoesNotHaveReactionInPreview(localIdentity, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
            await AssertIdentityDoesNotHaveReaction(localIdentity, uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(), reactionContent1);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestMethodNotAllowed))]
    public async Task GetReactionCountsByFile(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        const string reactionContent1 = ":k:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(addReactionResponse1.IsSuccessStatusCode);

        var addReactionResponse2 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(addReactionResponse2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.GetReactionCountsByFile(new GetReactionsRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier()
        });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var counts = response.Content;
            ClassicAssert.IsNotNull(counts);
            ClassicAssert.IsTrue(counts.Total == 2);
            ClassicAssert.IsNotNull(counts.Reactions.SingleOrDefault(r => r.ReactionContent == reactionContent1 && r.Count == 1));
            ClassicAssert.IsNotNull(counts.Reactions.SingleOrDefault(r => r.ReactionContent == reactionContent2 && r.Count == 1));
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestMethodNotAllowed))]
    public async Task CanGetReactionsByIdentity(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        const string reactionContent1 = ":k:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(addReactionResponse1.IsSuccessStatusCode);

        var addReactionResponse2 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(addReactionResponse2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.GetReactionsByIdentity(new GetReactionsByIdentityRequestRedux
        {
            Identity = identity.OdinId,
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier()
        });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var counts = response.Content;
            ClassicAssert.IsNotNull(counts);
            ClassicAssert.IsTrue(counts.Count == 2);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowedReactOnly))]
    [TestCaseSource(nameof(GuestMethodNotAllowed))]
    public async Task CanGetAllReactionsOnFile(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);
        var uploadResult = uploadMetadataResponse.Content;
        ClassicAssert.IsNotNull(uploadResult);

        const string reactionContent1 = ":k:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(addReactionResponse1.IsSuccessStatusCode);

        var addReactionResponse2 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        ClassicAssert.IsTrue(addReactionResponse2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.GetReactions(new GetReactionsRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Cursor = default,
            MaxRecords = 100
        });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var allReactions = response.Content;
            ClassicAssert.IsNotNull(allReactions);
            ClassicAssert.IsTrue(allReactions.Reactions.Count == 2);

            ClassicAssert.IsNotNull(allReactions.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent1 && r.OdinId == identity.OdinId && r.FileId.FileId == uploadResult.File.FileId));
            ClassicAssert.IsNotNull(allReactions.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent2 && r.OdinId == identity.OdinId && r.FileId.FileId == uploadResult.File.FileId));
        }
    }

    private async Task AssertIdentityDoesNotHaveReactionInPreview(TestIdentity identity, FileIdentifier fileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getHeaderResponse1 = await client.DriveRedux.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        var noMatchingInReactionPreview = file.FileMetadata.ReactionPreview.Reactions.All(pair => pair.Value.ReactionContent != reactionContent);
        ClassicAssert.IsTrue(noMatchingInReactionPreview);
    }

    private async Task AssertIdentityHasReactionInPreview(TestIdentity identity, FileIdentifier fileId, string reactionContent)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getHeaderResponse1 = await client.DriveRedux.QueryByGlobalTransitId(fileId.ToGlobalTransitIdFileIdentifier());

        var file = getHeaderResponse1.Content.SearchResults.First();
        var hasReactionInPreview = file.FileMetadata.ReactionPreview.Reactions.Any(pair => pair.Value.ReactionContent == reactionContent);
        ClassicAssert.IsTrue(hasReactionInPreview);
    }

    private async Task AssertIdentityHasReaction(TestIdentity identity, FileIdentifier globalTransitFileId, string reactionContent,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getReactionsResponse = await client.Reactions.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            fileSystemType);
        var hasReactionInDb = getReactionsResponse.Content.Reactions.Any(r => r.ReactionContent == reactionContent);
        ClassicAssert.IsTrue(hasReactionInDb);
    }

    private async Task AssertIdentityDoesNotHaveReaction(TestIdentity identity, FileIdentifier globalTransitFileId, string reactionContent,
        FileSystemType fileSystemType = FileSystemType.Standard)
    {
        var client = _scaffold.CreateOwnerApiClientRedux(identity);
        var getReactionsResponse = await client.Reactions.GetReactions(new GetReactionsRequestRedux
            {
                File = globalTransitFileId,
            },
            fileSystemType);
        var reactionNotInDb = getReactionsResponse.Content.Reactions.All(r => r.ReactionContent != reactionContent);
        ClassicAssert.IsTrue(reactionNotInDb);
    }
}