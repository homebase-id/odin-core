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

public class ReactionTests
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
    public async Task CanAddReactionToLocalIdentity(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
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
        var callerDriveClient = new UniversalDriveReactionClient(identity.OdinId, callerContext.GetFactory());
        const string reactionContent1 = ":cake:";
        var response = await callerDriveClient.AddReaction(new AddReactionRequestRedux
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

    public async Task CanUpdateReactionToLocalIdentity(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
    }

    public async Task CanDeleteReactionToLocalIdentity(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
    }
    
    public async Task CanDeleteReactionToLocalIdentity(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
    }

    // 
    // GetReactionCountsByFile
    // GetReactionsByIdentity
    // GetAllReactions
}