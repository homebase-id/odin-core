using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Statistics;

public class ReactionPreviewTests
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

        var commentFileUploadResult1 = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile1, "");

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

        var commentFileUploadResult2 = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile2, "");

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

        var commentFileUploadResult3 = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile3, "");

        // get the target blog file
        var blogPostHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Standard, uploadedContentResult.File);

        ClassicAssert.IsTrue(blogPostHeader.FileMetadata.ReactionPreview.Comments.Count == 3);
        ClassicAssert.IsTrue(blogPostHeader.FileMetadata.ReactionPreview.Reactions.Count == 0);
        ClassicAssert.IsTrue(blogPostHeader.FileMetadata.ReactionPreview.TotalCommentCount == 3);

        ClassicAssert.NotNull(blogPostHeader.FileMetadata.ReactionPreview.Comments.SingleOrDefault(x => x.Content == commentFile1.AppData.Content));
        ClassicAssert.NotNull(blogPostHeader.FileMetadata.ReactionPreview.Comments.SingleOrDefault(x => x.Content == commentFile2.AppData.Content));
        ClassicAssert.NotNull(blogPostHeader.FileMetadata.ReactionPreview.Comments.SingleOrDefault(x => x.Content == commentFile3.AppData.Content));
    }

    private async Task<UploadResult> UploadStandardFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = 200,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
    }
}