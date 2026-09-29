using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Serialization;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Reactions;

/// <summary>
/// Port of <c>OwnerApi/Drive/Statistics/ReactionPreviewTests</c>. Three comments posted against one
/// channel-drive post: the post's reaction preview must carry all three comments, a
/// <c>TotalCommentCount</c> of three, and no emoji reactions.
/// </summary>
/// <remarks>
/// No caller matrix — an <c>OwnerApi</c> fixture, so plain <c>[Test]</c> and <c>LoginAsOwner</c>;
/// the <c>SetupCallerWithOwner</c> ordering caveat does not apply. The V1
/// <c>OwnerApiClient.Drive</c> calls become <c>owner.V1.Drive</c> against the same V1 endpoints;
/// drive creation moves to <c>owner.Admin.CreateDrive</c>, whose <c>Metadata</c> default
/// (<c>string.Empty</c>) matches the original's <c>""</c> and which no assertion here reads. The
/// channel drive and the post upload are <see cref="ChannelPosts"/>, shared with
/// <see cref="ReactionTests"/>; the three comments keep their own metadata literals, which differ
/// from that fixture's.
/// <para>
/// The original passed <c>payloadData: ""</c> to <c>DriveApiClient.UploadFile</c>, which means "no
/// payload" — hence the metadata-only <c>UploadNewMetadata</c> here.
/// </para>
/// </remarks>
[TestFixture]
public class ReactionPreviewTests : V2Fixture
{
    [Test]
    public async Task AddingCommentUpdatesReactionPreview()
    {
        var frodoOwnerClient = await LoginAsOwner();

        //create a channel drive
        var frodoChannelDrive = await ChannelPosts.CreateChannelDriveAsync(frodoOwnerClient);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadedContentResult =
            await ChannelPosts.UploadPostAsync(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType: 200);

        //
        // Frodo posts a comment to his post
        //
        var commentFile1 = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = uploadedContentResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "a reply comment 1" }),
                FileType = 909,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        await ChannelPosts.UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, commentFile1);

        var commentFile2 = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = uploadedContentResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "a reply comment 2" }),
                FileType = 909,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        await ChannelPosts.UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, commentFile2);

        var commentFile3 = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = uploadedContentResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "a reply comment 3" }),
                FileType = 909,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        await ChannelPosts.UploadCommentAsync(frodoOwnerClient, frodoChannelDrive, commentFile3);

        // get the target blog file
        var blogPostHeader =
            (await frodoOwnerClient.V1.Drive.GetFileHeader(uploadedContentResult.File, FileSystemType.Standard)).Content;

        Assert.That(blogPostHeader, Is.Not.Null);
        Assert.That(blogPostHeader!.FileMetadata.ReactionPreview.Comments.Count, Is.EqualTo(3));
        Assert.That(blogPostHeader.FileMetadata.ReactionPreview.Reactions.Count, Is.EqualTo(0));
        Assert.That(blogPostHeader.FileMetadata.ReactionPreview.TotalCommentCount, Is.EqualTo(3));

        Assert.That(blogPostHeader.FileMetadata.ReactionPreview.Comments,
            Has.Exactly(1).Matches<CommentPreview>(x => x.Content == commentFile1.AppData.Content));
        Assert.That(blogPostHeader.FileMetadata.ReactionPreview.Comments,
            Has.Exactly(1).Matches<CommentPreview>(x => x.Content == commentFile2.AppData.Content));
        Assert.That(blogPostHeader.FileMetadata.ReactionPreview.Comments,
            Has.Exactly(1).Matches<CommentPreview>(x => x.Content == commentFile3.AppData.Content));
    }
}
