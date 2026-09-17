using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Tests;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;

namespace Odin.Hosting.Tests.V2.Ported.Comments;

/// <summary>
/// Port of <c>OwnerApi/Drive/CommentFileSystem/CommentFsOwnerTests</c>. The comment file system as
/// the owner sees it: a comment uploaded against a blog post comes back from the comment file
/// system with its own <see cref="FileSystemType"/>, deleting the comment decrements the blog
/// post's <c>TotalCommentCount</c>, and asking the standard file system for a comment file is
/// expected to fail.
/// </summary>
/// <remarks>
/// No caller matrix — an <c>OwnerApi</c> fixture, so plain <c>[Test]</c> methods and
/// <c>LoginAsOwner</c> only; the <c>SetupCallerWithOwner</c> ordering caveat does not apply.
/// <para>
/// The original drove <c>OwnerApiClient.Drive</c> (<c>DriveApiClient</c>), which is bound to
/// Kestrel via <c>OwnerApiTestUtils</c>. The port drives the same V1 endpoints through
/// <c>owner.V1.Drive</c>; the manifest and payload stream part are spelled out here so the request
/// stays byte-for-byte what <c>DriveApiClient.UploadFile</c> sent (payload descriptor with a null
/// <c>Iv</c>, stream part named for the payload key with content type
/// <c>application/x-binary</c>). Drive creation moves to <c>owner.Admin.CreateDrive</c>, whose
/// <c>Metadata</c> default (<c>string.Empty</c>) matches the original's <c>""</c>; no assertion here
/// reads the drive's name or metadata.
/// </para>
/// <para>
/// <b>Carried defect — <see cref="FailToGetCommentFromStandardFileSystem"/> asserts nothing.</b> The
/// body wraps the standard-file-system header read in a <c>try/catch</c> and only calls
/// <c>Assert.Pass</c> from the <c>catch</c>. Neither the original's client nor
/// <see cref="UniversalDriveApiClient.GetFileHeader"/> throws on a non-2xx — both hand back a Refit
/// <c>ApiResponse</c> — so the <c>catch</c> has never run and the test passes whether or not the
/// comment is readable from the standard file system. Carried verbatim rather than fixed inside a
/// port.
/// </para>
/// </remarks>
[TestFixture]
public class CommentFsOwnerTests : V2Fixture
{
    [Test]
    public async Task CanUploadComment()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Test Drive", allowAnonymousReads: false, ownerOnly: true);

        var blogMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 333,
                Content = "some blog content here but really in json format",
            }
        };

        var blogPostUploadResult = await UploadFile(owner, FileSystemType.Standard, drive, blogMetadata, "some payload");

        var commentMetadata = new UploadFileMetadata()
        {
            ReferencedFile = blogPostUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new UploadAppFileMetaData()
            {
                FileType = 10101,
                Content = "this is a comment about the blog post",
            }
        };

        var commentUploadResult = await UploadFile(owner, FileSystemType.Comment, drive, commentMetadata, "some payload");

        var commentFileHeaderResponse = await owner.V1.Drive.GetFileHeader(commentUploadResult.File, FileSystemType.Comment);
        var commentFileHeader = commentFileHeaderResponse.Content;

        Assert.That(commentFileHeader, Is.Not.Null);
        Assert.That(commentFileHeader!.ServerMetadata.FileSystemType, Is.EqualTo(FileSystemType.Comment));
        Assert.That(commentFileHeader.FileMetadata.AppData.Content, Is.EqualTo(commentMetadata.AppData.Content));
        Assert.That(commentFileHeader.FileMetadata.AppData.FileType, Is.EqualTo(commentMetadata.AppData.FileType));
    }

    [Test]
    public async Task ReactionPreviewUpdatedWhenCommentDeleted()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Test Drive", allowAnonymousReads: false, ownerOnly: true);

        var blogMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 333,
                Content = "some blog content here but really in json format",
            }
        };

        var blogPostUploadResult = await UploadFile(owner, FileSystemType.Standard, drive, blogMetadata, "some payload");

        var commentMetadata = new UploadFileMetadata()
        {
            ReferencedFile = blogPostUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new UploadAppFileMetaData()
            {
                FileType = 10101,
                Content = "this is a comment about the blog post",
            }
        };

        var commentUploadResult = await UploadFile(owner, FileSystemType.Comment, drive, commentMetadata, "some payload");

        var commentFileHeader = (await owner.V1.Drive.GetFileHeader(commentUploadResult.File, FileSystemType.Comment)).Content;

        Assert.That(commentFileHeader, Is.Not.Null);
        Assert.That(commentFileHeader!.ServerMetadata.FileSystemType, Is.EqualTo(FileSystemType.Comment));
        Assert.That(commentFileHeader.FileMetadata.AppData.Content, Is.EqualTo(commentMetadata.AppData.Content));
        Assert.That(commentFileHeader.FileMetadata.AppData.FileType, Is.EqualTo(commentMetadata.AppData.FileType));

        var blogPostHeaderWith1Comment =
            (await owner.V1.Drive.GetFileHeader(blogPostUploadResult.File, FileSystemType.Standard)).Content;

        Assert.That(blogPostHeaderWith1Comment, Is.Not.Null);
        Assert.That(blogPostHeaderWith1Comment!.FileMetadata.ReactionPreview.TotalCommentCount, Is.EqualTo(1));

        //
        // Now delete the comment
        //
        await owner.V1.Drive.SoftDeleteFile(commentUploadResult.File, null, FileSystemType.Comment);

        var updatedBlogPostHeader =
            (await owner.V1.Drive.GetFileHeader(blogPostUploadResult.File, FileSystemType.Standard)).Content;

        Assert.That(updatedBlogPostHeader, Is.Not.Null);
        Assert.That(updatedBlogPostHeader!.FileMetadata.ReactionPreview.TotalCommentCount, Is.EqualTo(0));
    }

    [Test]
    public async Task FailToGetCommentFromStandardFileSystem()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Test Drive", allowAnonymousReads: false, ownerOnly: true);

        var blogMetadata = new UploadFileMetadata()
        {
            AppData = new UploadAppFileMetaData()
            {
                FileType = 333,
                Content = "some blog content here but really in json format",
            }
        };

        var blogPostUploadResult = await UploadFile(owner, FileSystemType.Standard, drive, blogMetadata, "some payload");

        var commentMetadata = new UploadFileMetadata()
        {
            ReferencedFile = blogPostUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new UploadAppFileMetaData()
            {
                FileType = 10101,
                Content = "this is a comment about the blog post",
            }
        };

        var commentUploadResult = await UploadFile(owner, FileSystemType.Comment, drive, commentMetadata, "some payload");

        try
        {
            var _ = await owner.V1.Drive.GetFileHeader(commentUploadResult.File, FileSystemType.Standard);
        }
        catch (Exception)
        {
            Assert.Pass("Exception throw as expected");
        }
    }

    /// <summary>
    /// The original's <c>DriveApiClient.UploadFile(fileSystemType, drive, metadata, payloadData,
    /// payloadKey)</c>: one payload, no thumbnails, manifest descriptor with a null <c>Iv</c>.
    /// Throws if the upload is refused — the original's client asserted success inline.
    /// </summary>
    private static async Task<UploadResult> UploadFile(
        OwnerSession owner,
        FileSystemType fileSystemType,
        TargetDrive targetDrive,
        UploadFileMetadata fileMetadata,
        string payloadData)
    {
        var manifest = new UploadManifest()
        {
            PayloadDescriptors = new List<UploadManifestPayloadDescriptor>()
            {
                new()
                {
                    Iv = null,
                    PayloadKey = WebScaffold.PAYLOAD_KEY,
                    Thumbnails = new List<UploadedManifestThumbnailDescriptor>()
                }
            }
        };

        var payloads = new List<TestPayloadDefinition>()
        {
            new()
            {
                Iv = null,
                Key = WebScaffold.PAYLOAD_KEY,
                ContentType = "application/x-binary",
                Content = payloadData.ToUtf8ByteArray(),
                Thumbnails = new List<ThumbnailContent>()
            }
        };

        var response = await owner.V1.Drive.UploadNewFile(targetDrive, fileMetadata, manifest, payloads,
            fileSystemType: fileSystemType);

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        return response.Content!;
    }
}
