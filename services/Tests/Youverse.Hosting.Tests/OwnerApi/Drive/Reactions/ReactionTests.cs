using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.Reactions;

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

    [Test]
    public async Task CanUploadCommentOwnerDrive()
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
        var uploadResult = await UploadToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        //
        // Frodo posts feedback to his post
        //
        var comment = "Indeed, Indeed I am Mr. Underhill";
        var targetReferenceFile = uploadResult.File;
        var commentUploadResult = await UploadComment(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, comment, false);

        Assert.IsTrue(uploadResult.File.TargetDrive == commentUploadResult.File.TargetDrive);

        var commentFileHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Comment, commentUploadResult.File);

        Assert.IsTrue(commentFileHeader.FileId == commentUploadResult.File.FileId);
        Assert.IsTrue(commentFileHeader.FileMetadata.AppData.JsonContent == comment);
        Assert.IsTrue(commentFileHeader.FileMetadata.ReferencedFile == targetReferenceFile, "target reference file not referenced");
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
        var uploadedContentResult = await UploadToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        //
        // Frodo posts feedback to his post
        //
        var comment = "Indeed, Indeed I am Mr. Underhill";
        var commentUploadResult = await UploadComment(frodoOwnerClient, frodoChannelDrive, uploadedContentResult.File, comment, false);

        Assert.IsTrue(uploadedContentResult.File.TargetDrive == commentUploadResult.File.TargetDrive, "Drive for content file and reaction must match");

        var commentFileHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Comment, commentUploadResult.File);

        Assert.IsTrue(commentFileHeader.FileId == commentUploadResult.File.FileId);
        Assert.IsTrue(commentFileHeader.FileMetadata.AppData.JsonContent == comment);
        Assert.IsTrue(commentFileHeader.FileMetadata.ReferencedFile == uploadedContentResult.File, "target reference file not referenced");

        // Get the target file and validate reaction was updated

        var targetFileHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Standard, uploadedContentResult.File);
        Assert.IsNotNull(targetFileHeader);
        Assert.IsNotNull(targetFileHeader.ReactionPreview);
        Assert.IsTrue(targetFileHeader.ReactionPreview.Comments.Any(c => c.JsonContent == comment));
    }

    [Test]
    [Description("Tests that the owner can upload a file, upload feedback for that file, the find it via search index")]
    public async Task CanGetAllCommentsForAForOwnerPost()
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
        var uploadResult = await UploadToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        var targetReferenceFile = uploadResult.File;

        //
        // Frodo posts the first comment
        //
        var commentContent1 = "Indeed, Indeed I am Mr. Underhill";
        var commentUploadResult = await UploadComment(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, commentContent1, false);
        Assert.IsTrue(uploadResult.File.TargetDrive == commentUploadResult.File.TargetDrive);

        //
        // Frodo posts the second
        //
        var commentContent2 = "Totes agreeing with myself";
        var commentUploadResult2 = await UploadComment(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, commentContent2, false);
        Assert.IsTrue(uploadResult.File.TargetDrive == commentUploadResult2.File.TargetDrive);


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
        // Assert.IsTrue(feedbackSearchResults.SearchResults.Count() == 2);
        // Assert.IsNotNull(feedbackSearchResults.SearchResults.SingleOrDefault(fb => fb.FileMetadata.AppData.JsonContent == feedbackContent1));
        // Assert.IsNotNull(feedbackSearchResults.SearchResults.SingleOrDefault(fb => fb.FileMetadata.AppData.JsonContent == feedbackContent2));
    }


    [Test]
    [Ignore("waiting for endpoint")]
    public async Task CanSendEmojiReaction()
    {
        // var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        //
        // //create a channel drive
        // var frodoChannelDrive = new TargetDrive()
        // {
        //     Alias = Guid.NewGuid(),
        //     Type = SystemDriveConstants.ChannelDriveType
        // };
        //
        // await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);
        //
        // // Frodo uploads content to channel drive
        // var uploadedContent = "I'm Mr. Underhill";
        // var uploadResult = await UploadToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);
        //
        //
    }


    private async Task<UploadResult> UploadToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, bool allowDistribution = true)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = allowDistribution,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
    }

    private async Task<UploadResult> UploadComment(OwnerApiClient client, TargetDrive targetDrive, ExternalFileIdentifier referencedFile, string commentContent, bool allowDistribution)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = allowDistribution,
            ContentType = "application/json",
            PayloadIsEncrypted = false,

            //indicates the file about which this file is giving feed back
            ReferencedFile = referencedFile,

            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = commentContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Comment, targetDrive, fileMetadata);
    }

}