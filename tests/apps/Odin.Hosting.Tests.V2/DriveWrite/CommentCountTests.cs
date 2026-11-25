using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.DriveWrite;

/// <summary>
/// A comment's count on the file it comments on is changed as a reaction's is: in one read inside storage, asking the
/// drive's <c>React</c>, <c>Comment</c> or <c>Write</c>, not the read rule (#2070).
/// </summary>
[TestFixture]
public class CommentCountTests : V2Fixture
{
    [Test]
    public async Task ACommentOnAFileItsAuthorCannotReadIsCounted()
    {
        var spec = CallerSpec.Guest(DriveSpec.Secured(), DrivePermission.Read | DrivePermission.Comment);
        var (guest, owner) = await SetupCallerWithOwner(spec);
        var post = await AppFileUploads.SeedTextFileAsync(owner, spec.TargetDrive, "owner only", AccessControlList.OwnerOnly);

        var metadata = SampleMetadataData.CreateWithContent(fileType: 101, "a guest's comment", AccessControlList.OwnerOnly);
        metadata.ReferencedFile = new GlobalTransitIdFileIdentifier { TargetDrive = spec.TargetDrive, GlobalTransitId = post.GlobalTransitId!.Value };
        var comment = await new DriveWriterV2Client(guest.Identity, guest.Factory, FileSystemType.Comment)
            .UploadNewMetadata(spec.TargetDrive.Alias, metadata);
        Assert.That(comment.StatusCode, Is.EqualTo(HttpStatusCode.OK), comment.Error?.Content);

        var header = await AppFileUploads.HeaderAsync(owner, spec.TargetDrive, post.FileId);
        Assert.That(header.FileMetadata.ReactionPreview?.TotalCommentCount, Is.EqualTo(1));
    }
}
