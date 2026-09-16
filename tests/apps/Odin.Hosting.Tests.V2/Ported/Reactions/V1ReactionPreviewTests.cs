using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.Base.Drive.GroupReactions;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Reactions;

/// <summary>
/// Port of <c>_Universal/DriveTests/ReactionPreview/DirectDriveReactionPreviewTests</c>. A reaction
/// preview is derived state the server maintains alongside the header, so overwriting the header must
/// not take it with it: after an ordinary metadata update the new content is there and the reaction
/// preview is still intact.
/// </summary>
/// <remarks>
/// Drives the <b>V1</b> drive and group-reaction endpoints through the in-process host via
/// <see cref="UniversalDriveApiClient"/> and <see cref="UniversalDriveReactionClient"/>, reached
/// through <c>owner.V1.Drive</c> / <c>owner.V1.Reactions</c>.
///
/// No caller matrix: the original had none either — it is a plain <c>[Test]</c> run entirely as the
/// owner, so the <c>SetupCallerWithOwner</c> ordering caveat does not apply. Everything is local to
/// one identity (<c>TransitOptions</c> is null), so the fixture runs on the default host identity
/// rather than the original's Pippin.
///
/// The <c>V1</c> prefix is only about the class name: <see cref="ReactionPreviewTests"/> in this folder
/// is a different fixture, ported from <c>OwnerApi/Drive/Statistics</c> and about comment previews.
/// </remarks>
[TestFixture]
public class V1ReactionPreviewTests : V2Fixture
{
    [Test]
    public async Task CanUpdateHeaderAndKeepReactionPreview()
    {
        // Setup
        var owner = await LoginAsOwner();
        var targetDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(targetDrive, "Test Drive 001", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        var uploadMetadataResponse = await owner.V1.Drive.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        var uploadResult = uploadMetadataResponse.Content;

        const string reactionContent1 = ":k:";
        var request = new AddReactionRequestRedux
        {
            File = uploadResult.GlobalTransitIdFileIdentifier.ToFileIdentifier(),
            Reaction = reactionContent1,
            TransitOptions = null
        };

        var addReactionResponse = await owner.V1.Reactions.AddReaction(request);
        Assert.That(addReactionResponse.IsSuccessStatusCode, Is.True);

        // Validate the reaction is there (get file)
        var getHeaderResponse1 = await owner.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse1.Content.FileMetadata.ReactionPreview.Reactions
            .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1), Is.Not.Null);

        // update the same file
        uploadedFileMetadata.AppData.Content = "changed data";
        var updateResponse = await owner.V1.Drive.UpdateExistingMetadata(uploadResult.File,
            getHeaderResponse1.Content.FileMetadata.VersionTag, uploadedFileMetadata);
        Assert.That(updateResponse.IsSuccessStatusCode, Is.True);

        // Validate the reaction is there (get file)
        var getHeaderResponse2 = await owner.V1.Drive.GetFileHeader(uploadResult.File);
        Assert.That(getHeaderResponse2.Content.FileMetadata.AppData.Content, Is.EqualTo("changed data"));
        Assert.That(getHeaderResponse2.Content.FileMetadata.ReactionPreview.Reactions
            .SingleOrDefault(pair => pair.Value.ReactionContent == reactionContent1), Is.Not.Null);
    }
}
