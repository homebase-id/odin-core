using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._Universal.DriveTests.ReactionPreview;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveReactionPreviewTests
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
    public async Task CanUpdateHeaderAndKeepReactionPreview()
    {
        // Setup
        var pippinIdentity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(pippinIdentity);
        var targetDrive = TargetDrive.NewTargetDrive();
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        var uploadResult = uploadMetadataResponse.Content;

        const string reactionContent1 = ":k:";
        var request = new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        };
        
        var addReactionResponse = await ownerApiClient.Reactions.AddReaction(request);
        ClassicAssert.IsTrue(addReactionResponse.IsSuccessStatusCode);

        // Validate the reaction is there (get file)
        var getHeaderResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsNotNull(getHeaderResponse1.Content.FileMetadata.ReactionPreview.Reactions
            .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1));

        // update the same file
        uploadedFileMetadata.AppData.Content = "changed data";
        var updateResponse = await ownerApiClient.DriveRedux.UpdateExistingMetadata(uploadResult.File, getHeaderResponse1.Content.FileMetadata.VersionTag, uploadedFileMetadata);
        ClassicAssert.IsTrue(updateResponse.IsSuccessStatusCode);
        
        // Validate the reaction is there (get file)
        var getHeaderResponse2 = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getHeaderResponse2.Content.FileMetadata.AppData.Content == "changed data");
        ClassicAssert.IsNotNull(
            getHeaderResponse2.Content.FileMetadata.ReactionPreview.Reactions
                .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1));
    }
}