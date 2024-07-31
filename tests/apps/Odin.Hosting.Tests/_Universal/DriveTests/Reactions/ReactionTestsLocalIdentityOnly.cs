using System.Collections;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Drive.ReactionsRedux;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
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
        Assert.IsNotNull(uploadResult);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        const string reactionContent1 = ":cake:";
        var response = await callerReactionClient.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            // Validate the reaction is there (get file)
            var getHeaderResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
            Assert.IsNotNull(getHeaderResponse1.Content.FileMetadata.ReactionPreview.Reactions
                .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1));
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanDeleteReaction(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
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

        const string reactionContent1 = ":cake:";

        var addReactionResponse = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.DeleteReaction(new DeleteReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var getHeaderResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
            var hasReactionInPreview = getHeaderResponse1.Content.FileMetadata.ReactionPreview.Reactions
                .All(pair => pair.Value.ReactionContent != reactionContent1);
            Assert.IsFalse(hasReactionInPreview);

            var getReactionsResponse = await ownerApiClient.Reactions.GetAllReactions(uploadResult.File.ToFileIdentifier());
            var hasReactionInDb = getReactionsResponse.Content.Reactions.All(r => r.ReactionContent != reactionContent1);
            Assert.IsFalse(hasReactionInDb);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanDeleteAllReactionsOnFile(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
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

        const string reactionContent1 = ":cake:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse1.IsSuccessStatusCode);

        var addReactionResponse2 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.DeleteAllReactionsOnFile(new DeleteReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier()
        });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var getHeaderResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
            var hasReactionInPreview = getHeaderResponse1.Content.FileMetadata.ReactionPreview.Reactions
                .All(pair => pair.Value.ReactionContent != reactionContent1 || pair.Value.ReactionContent != reactionContent2);
            Assert.IsTrue(hasReactionInPreview);

            var getReactionsResponse = await ownerApiClient.Reactions.GetAllReactions(uploadResult.File.ToFileIdentifier());
            var hasReactionInDb =
                getReactionsResponse.Content.Reactions.All(r => r.ReactionContent != reactionContent1 || r.ReactionContent != reactionContent2);
            Assert.IsFalse(hasReactionInDb);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
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
        Assert.IsNotNull(uploadResult);

        const string reactionContent1 = ":cake:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse1.IsSuccessStatusCode);

        var addReactionResponse2 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.GetReactionCountsByFile(new GetReactionsRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier()
        });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var counts = response.Content;
            Assert.IsNotNull(counts);
            Assert.IsTrue(counts.Total == 2);
            Assert.IsNotNull(counts.Reactions.SingleOrDefault(r => r.ReactionContent == reactionContent1 && r.Count == 1));
            Assert.IsNotNull(counts.Reactions.SingleOrDefault(r => r.ReactionContent == reactionContent2 && r.Count == 1));
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
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
        Assert.IsNotNull(uploadResult);

        const string reactionContent1 = ":cake:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse1.IsSuccessStatusCode);

        var addReactionResponse2 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.GetReactionsByIdentity(new GetReactionsByIdentityRequestRedux
        {
            Identity = identity.OdinId,
            File = uploadResult.File.ToFileIdentifier()
        });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var counts = response.Content;
            Assert.IsNotNull(counts);
            Assert.IsTrue(counts.Count == 2);
        }
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
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
        Assert.IsNotNull(uploadResult);

        const string reactionContent1 = ":cake:";
        const string reactionContent2 = ":pie:";

        var addReactionResponse1 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse1.IsSuccessStatusCode);

        var addReactionResponse2 = await ownerApiClient.Reactions.AddReaction(new AddReactionRequestRedux
        {
            File = uploadResult.File.ToFileIdentifier(),
            Reaction = reactionContent2,
            TransitOptions = null
        });
        Assert.IsTrue(addReactionResponse2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var callerReactionClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerReactionClient.GetAllReactions(uploadResult.File.ToFileIdentifier());

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var allReactions = response.Content;
            Assert.IsNotNull(allReactions);
            Assert.IsTrue(allReactions.Reactions.Count == 2);

            Assert.IsNotNull(allReactions.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent1 && r.OdinId == identity.OdinId && r.FileId.FileId == uploadResult.File.FileId));
            Assert.IsNotNull(allReactions.Reactions.SingleOrDefault(r =>
                r.ReactionContent == reactionContent2 && r.OdinId == identity.OdinId && r.FileId.FileId == uploadResult.File.FileId));
        }
    }
}