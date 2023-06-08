using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.Drive.Statistics;

public class ReactionPreviewTests
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

    [Test]
    public async Task AddingCommentUpdatesReactionPreview()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadedContentResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        //
        // Frodo posts a comment to his post
        //
        var commentFile1 = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            ReferencedFile = uploadedContentResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = OdinSystemSerializer.Serialize(new { message = "a reply comment 1" }),
                FileType = 909,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        var commentFileUploadResult1 = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile1, "");

        var commentFile2 = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            ReferencedFile = uploadedContentResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = OdinSystemSerializer.Serialize(new { message = "a reply comment 2" }),
                FileType = 909,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        var commentFileUploadResult2 = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile2, "");

        var commentFile3 = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            ReferencedFile = uploadedContentResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = OdinSystemSerializer.Serialize(new { message = "a reply comment 3" }),
                FileType = 909,
                DataType = 202,
                UserDate = new UnixTimeUtc(0),
                Tags = default
            }
        };

        var commentFileUploadResult3 = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile3, "");

        // get the target blog file
        var blogPostHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Standard, uploadedContentResult.File);

        Assert.IsTrue(blogPostHeader.FileMetadata.ReactionPreview.Comments.Count == 3);
        Assert.IsTrue(blogPostHeader.FileMetadata.ReactionPreview.Reactions.Count == 0);
        Assert.IsTrue(blogPostHeader.FileMetadata.ReactionPreview.TotalCommentCount == 3);

        Assert.NotNull(blogPostHeader.FileMetadata.ReactionPreview.Comments.SingleOrDefault(x => x.JsonContent == commentFile1.AppData.JsonContent));
        Assert.NotNull(blogPostHeader.FileMetadata.ReactionPreview.Comments.SingleOrDefault(x => x.JsonContent == commentFile2.AppData.JsonContent));
        Assert.NotNull(blogPostHeader.FileMetadata.ReactionPreview.Comments.SingleOrDefault(x => x.JsonContent == commentFile3.AppData.JsonContent));
    }

    private async Task<UploadResult> UploadStandardFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = uploadedContent,
                FileType = 200,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
    }
}