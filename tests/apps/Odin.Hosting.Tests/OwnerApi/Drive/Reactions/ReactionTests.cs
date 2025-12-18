using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Reactions;

public class ReactionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo });
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
        // var targetReferenceFile = uploadResult.File;
        var targetReferenceFile = new GlobalTransitIdFileIdentifier()
        {
            GlobalTransitId = uploadResult.GlobalTransitId.GetValueOrDefault(),
            TargetDrive = uploadResult.File.TargetDrive
        };
        
        var commentUploadResult = await UploadComment(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, comment, false);

        ClassicAssert.IsTrue(uploadResult.File.TargetDrive == commentUploadResult.File.TargetDrive);

        var commentFileHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Comment, commentUploadResult.File);

        ClassicAssert.IsTrue(commentFileHeader.FileId == commentUploadResult.File.FileId);
        ClassicAssert.IsTrue(commentFileHeader.FileMetadata.AppData.Content == comment);
        ClassicAssert.IsTrue(commentFileHeader.FileMetadata.ReferencedFile == targetReferenceFile, "target reference file not referenced");
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
        var commentUploadResult = await UploadComment(frodoOwnerClient, frodoChannelDrive, uploadedContentResult.GlobalTransitIdFileIdentifier, comment, false);

        ClassicAssert.IsTrue(uploadedContentResult.File.TargetDrive == commentUploadResult.File.TargetDrive, "Drive for content file and reaction must match");

        var commentFileHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Comment, commentUploadResult.File);

        ClassicAssert.IsTrue(commentFileHeader.FileId == commentUploadResult.File.FileId);
        ClassicAssert.IsTrue(commentFileHeader.FileMetadata.AppData.Content == comment);
        ClassicAssert.IsTrue(commentFileHeader.FileMetadata.ReferencedFile == uploadedContentResult.GlobalTransitIdFileIdentifier, "target reference file not referenced");

        // Get the target file and validate reaction was updated

        var targetFileHeader = await frodoOwnerClient.Drive.GetFileHeader(FileSystemType.Standard, uploadedContentResult.File);
        ClassicAssert.IsNotNull(targetFileHeader);
        ClassicAssert.IsNotNull(targetFileHeader.FileMetadata.ReactionPreview);
        ClassicAssert.IsTrue(targetFileHeader.FileMetadata.ReactionPreview.Comments.Any(c => c.Content == comment));
    }

    [Test, Explicit("TODO")]
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

        var targetReferenceFile = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Frodo posts the first comment
        //
        var commentContent1 = "Indeed, Indeed I am Mr. Underhill";
        var commentUploadResult = await UploadComment(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, commentContent1, false);
        ClassicAssert.IsTrue(uploadResult.File.TargetDrive == commentUploadResult.File.TargetDrive);

        //
        // Frodo posts the second
        //
        var commentContent2 = "Totes agreeing with myself";
        var commentUploadResult2 = await UploadComment(frodoOwnerClient, frodoChannelDrive, targetReferenceFile, commentContent2, false);
        ClassicAssert.IsTrue(uploadResult.File.TargetDrive == commentUploadResult2.File.TargetDrive);


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

    private async Task<UploadResult> UploadToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, bool allowDistribution = true)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = allowDistribution,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata);
    }

    private async Task<UploadResult> UploadComment(OwnerApiClient client, TargetDrive targetDrive, GlobalTransitIdFileIdentifier referencedFile, string commentContent, bool allowDistribution)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = allowDistribution,
            IsEncrypted = false,

            //indicates the file about which this file is giving feed back
            ReferencedFile = referencedFile,

            AppData = new()
            {
                Content = commentContent,
                FileType = default,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Comment, targetDrive, fileMetadata);
    }

}