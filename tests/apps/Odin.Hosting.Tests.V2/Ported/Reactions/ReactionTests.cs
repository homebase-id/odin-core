using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Reactions;

/// <summary>
/// Port of <c>OwnerApi/Drive/Reactions/ReactionTests</c>. Comments posted by the owner against his
/// own channel-drive post: the comment lands on the same drive as the post, comes back from the
/// comment file system with its <c>ReferencedFile</c> intact, and shows up in the post's reaction
/// preview.
/// </summary>
/// <remarks>
/// No caller matrix — an <c>OwnerApi</c> fixture, so plain <c>[Test]</c> methods and
/// <c>LoginAsOwner</c>; the <c>SetupCallerWithOwner</c> ordering caveat does not apply. The V1
/// <c>OwnerApiClient.Drive</c> calls become <c>owner.V1.Drive</c> against the same V1 endpoints;
/// drive creation moves to <c>owner.Admin.CreateDrive</c>, whose <c>Metadata</c> default
/// (<c>string.Empty</c>) matches the original's <c>""</c> and which no assertion here reads. The
/// channel drive and the post/comment uploads are <see cref="ChannelPosts"/>, shared with
/// <see cref="ReactionPreviewTests"/>.
/// <para>
/// Despite the fixture's name nothing here posts an emoji reaction; every test posts a
/// <see cref="FileSystemType.Comment"/> file. Name kept as the original's.
/// </para>
/// <para>
/// <c>CanGetAllCommentsForAForOwnerPost</c> keeps its <c>[Explicit("TODO")]</c> and its terminal
/// <c>Assert.Inconclusive</c> verbatim — it is excluded from <c>dotnet test</c> runs, which run in
/// NUnit's Non-Explicit mode.
/// </para>
/// </remarks>
[TestFixture]
public class ReactionTests : V2Fixture
{
    [Test]
    public async Task CanUploadCommentOwnerDrive()
    {
        var frodoOwnerClient = await LoginAsOwner();

        //create a channel drive
        var frodoChannelDrive = await ChannelPosts.CreateChannelDriveAsync(frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await ChannelPosts.UploadPostAsync(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        //
        // Frodo posts feedback to his post
        //
        var comment = "Indeed, Indeed I am Mr. Underhill";
        // var targetReferenceFile = uploadResult.File;
        var targetReferenceFile = new GlobalTransitIdFileIdentifier()
        {
            GlobalTransitId = uploadResult.GlobalTransitId.GetValueOrDefault(),
            TargetDrive = uploadResult.File.TargetDrive
        };

        var commentUploadResult =
            await ChannelPosts.UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, comment);

        Assert.That(commentUploadResult.File.TargetDrive, Is.EqualTo(uploadResult.File.TargetDrive));

        var commentFileHeader =
            (await frodoOwnerClient.V1.Drive.GetFileHeader(commentUploadResult.File, FileSystemType.Comment)).Content;

        Assert.That(commentFileHeader, Is.Not.Null);
        Assert.That(commentFileHeader!.FileId, Is.EqualTo(commentUploadResult.File.FileId));
        Assert.That(commentFileHeader.FileMetadata.AppData.Content, Is.EqualTo(comment));
        Assert.That(commentFileHeader.FileMetadata.ReferencedFile, Is.EqualTo(targetReferenceFile),
            "target reference file not referenced");
    }

    [Test]
    public async Task AddingCommentUpdatesReactionPreview()
    {
        var frodoOwnerClient = await LoginAsOwner();

        //create a channel drive
        var frodoChannelDrive = await ChannelPosts.CreateChannelDriveAsync(frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadedContentResult = await ChannelPosts.UploadPostAsync(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        //
        // Frodo posts feedback to his post
        //
        var comment = "Indeed, Indeed I am Mr. Underhill";
        var commentUploadResult = await ChannelPosts.UploadCommentAsync(frodoOwnerClient, frodoChannelDrive,
            uploadedContentResult.GlobalTransitIdFileIdentifier, comment);

        Assert.That(commentUploadResult.File.TargetDrive, Is.EqualTo(uploadedContentResult.File.TargetDrive),
            "Drive for content file and reaction must match");

        var commentFileHeader =
            (await frodoOwnerClient.V1.Drive.GetFileHeader(commentUploadResult.File, FileSystemType.Comment)).Content;

        Assert.That(commentFileHeader, Is.Not.Null);
        Assert.That(commentFileHeader!.FileId, Is.EqualTo(commentUploadResult.File.FileId));
        Assert.That(commentFileHeader.FileMetadata.AppData.Content, Is.EqualTo(comment));
        Assert.That(commentFileHeader.FileMetadata.ReferencedFile,
            Is.EqualTo(uploadedContentResult.GlobalTransitIdFileIdentifier), "target reference file not referenced");

        // Get the target file and validate reaction was updated

        var targetFileHeader =
            (await frodoOwnerClient.V1.Drive.GetFileHeader(uploadedContentResult.File, FileSystemType.Standard)).Content;

        Assert.That(targetFileHeader, Is.Not.Null);
        Assert.That(targetFileHeader!.FileMetadata.ReactionPreview, Is.Not.Null);
        Assert.That(targetFileHeader.FileMetadata.ReactionPreview.Comments.Any(c => c.Content == comment), Is.True);
    }

    [Test, Explicit("TODO")]
    [Description("Tests that the owner can upload a file, upload feedback for that file, the find it via search index")]
    public async Task CanGetAllCommentsForAForOwnerPost()
    {
        var frodoOwnerClient = await LoginAsOwner();

        //create a channel drive
        var frodoChannelDrive = await ChannelPosts.CreateChannelDriveAsync(frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await ChannelPosts.UploadPostAsync(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        var targetReferenceFile = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Frodo posts the first comment
        //
        var commentContent1 = "Indeed, Indeed I am Mr. Underhill";
        var commentUploadResult =
            await ChannelPosts.UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, commentContent1);
        Assert.That(commentUploadResult.File.TargetDrive, Is.EqualTo(uploadResult.File.TargetDrive));

        //
        // Frodo posts the second
        //
        var commentContent2 = "Totes agreeing with myself";
        var commentUploadResult2 =
            await ChannelPosts.UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, commentContent2);
        Assert.That(commentUploadResult2.File.TargetDrive, Is.EqualTo(uploadResult.File.TargetDrive));

        //
        // Frodo can find the feedback files for the original posted file
        //
        Assert.Inconclusive("Need to determine how we're accessing text reactions");
        // var feedbackSearchResults = await frodoOwnerClient.Drive.QueryBatch(new FileQueryParams()
        // {
        //     TargetDrive = frodoChannelDrive,
        //     FileType = new[] { ReservedFileTypes.DataFeedback },
        //     // ReferenceToFile
        // });
        //
        // ClassicAssert.IsTrue(feedbackSearchResults.SearchResults.Count() == 2);
        // ClassicAssert.IsNotNull(feedbackSearchResults.SearchResults.SingleOrDefault(fb => fb.FileMetadata.AppData.JsonContent == feedbackContent1));
        // ClassicAssert.IsNotNull(feedbackSearchResults.SearchResults.SingleOrDefault(fb => fb.FileMetadata.AppData.JsonContent == feedbackContent2));
    }
}
