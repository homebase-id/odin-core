using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.AppAPI.ChatStructure.Api;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.DataSubscription;

public class DataSubscriptionTests
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
    public async Task CanUploadStandardFileToDriveAndDistributeToFollower()
    {
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, 101);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { 101 }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }


    [Test]
    public async Task CanUpdateStandardFileAndDistributeChangesForAllNotifications()
    {
        const int fileType = 2001;
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill; I think";
        var uploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { fileType }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var originalFile = batch.SearchResults.First();
        Assert.IsTrue(originalFile.FileState == FileState.Active);
        Assert.IsTrue(originalFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(originalFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //Now change the file as if someone edited a post

        var updatedContent = "No really, I'm Frodo Baggins";
        var uploadResult2 = await OverwriteStandardFile(
            client: frodoOwnerClient,
            overwriteFile: uploadResult.File,
            updatedContent, fileType);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        //Sam should have changes; note - we're using the same query params intentionally
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch2.SearchResults.Count() == 1, $"Count should be 1 but was {batch2.SearchResults.Count()}");
        var updatedFile = batch2.SearchResults.First();
        Assert.IsTrue(updatedFile.FileState == FileState.Active);
        Assert.IsTrue(updatedFile.FileMetadata.Created == originalFile.FileMetadata.Created);
        Assert.IsTrue(updatedFile.FileMetadata.Updated > originalFile.FileMetadata.Updated);
        Assert.IsTrue(updatedFile.FileMetadata.AppData.JsonContent == updatedContent);
        Assert.IsTrue(updatedFile.FileMetadata.AppData.JsonContent != originalFile.FileMetadata.AppData.JsonContent);
        Assert.IsTrue(updatedFile.FileMetadata.GlobalTransitId == originalFile.FileMetadata.GlobalTransitId);

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    [Ignore("need to support deleting linked files for followers ")]
    public async Task CanUploadStandardFileThenDeleteThenDistributeDeletion()
    {
        const int fileType = 1117;
        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill; I think";
        var standardFileUploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { fileType }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var originalFile = batch.SearchResults.First();
        Assert.IsTrue(originalFile.FileState == FileState.Active);
        Assert.IsTrue(originalFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(originalFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Frodo now deletes the file
        await frodoOwnerClient.Drive.DeleteFile(FileSystemType.Standard, standardFileUploadResult.File);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);
        
        //Sam should have the file marked as deleted
        var deletedFile = await samOwnerClient.Drive.GetFileHeader(FileSystemType.Standard, standardFileUploadResult.File);
        Assert.IsTrue(deletedFile.FileState == FileState.Deleted, "File should be deleted");

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }
    
    [Test]
    public async Task CanUploadCommentFileToDriveAndDistributeToFollower()
    {
        const int standardFileType = 2773;
        const int commentFileType = 999;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { standardFileType }
        };

        // Sam should have the blog post
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            ReferencedFile = standardFileUploadResult.File,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(new { message = "a reply comment" }),
                FileType = commentFileType,
                DataType = 202,
                UserDate = 0,
                Tags = default
            }
        };

        var commentFileUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile, "");

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        // Sam should have the comment
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(commentBatch.SearchResults.Count() == 1);
        var theCommentFile = commentBatch.SearchResults.First();
        Assert.IsTrue(theCommentFile.FileState == FileState.Active);
        Assert.IsTrue(theCommentFile.FileMetadata.AppData.JsonContent == commentFile.AppData.JsonContent);
        Assert.IsTrue(theCommentFile.FileMetadata.GlobalTransitId == commentFileUploadResult.GlobalTransitId);


        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task CanUpdateCommentFileDistributeChangesForAllNotifications()
    {
        const int standardFileType = 332;
        const int commentFileType = 1113;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, ownerOnly: false, allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { standardFileType }
        };

        // Sam should have the blog post
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            ReferencedFile = standardFileUploadResult.File,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(new { message = "Are you tho?" }),
                FileType = commentFileType,
                DataType = 202,
                UserDate = 0,
                Tags = default
            }
        };

        var originalCommentUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile, "");

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        // Sam should have the comment
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(commentBatch.SearchResults.Count() == 1);
        var originalCommentFile = commentBatch.SearchResults.First();
        Assert.IsTrue(originalCommentFile.FileState == FileState.Active);
        Assert.IsTrue(originalCommentFile.FileMetadata.AppData.JsonContent == commentFile.AppData.JsonContent);
        Assert.IsTrue(originalCommentFile.FileMetadata.GlobalTransitId == originalCommentUploadResult.GlobalTransitId);


        //Edit the comment and re-upload

        var updatedComment = DotYouSystemSerializer.Serialize(new { message = "Are you tho... Mr Baggins?" });
        commentFile.AppData.JsonContent = updatedComment;

        var _ = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile,
            overwriteFileId: originalCommentUploadResult.File.FileId);

        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        // Sam should have the updated comment
        var secondCommentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(secondCommentBatch.SearchResults.Count() == 1);
        var updatedCommentFile = secondCommentBatch.SearchResults.First();
        Assert.IsTrue(updatedCommentFile.FileState == FileState.Active);
        Assert.IsTrue(updatedCommentFile.FileMetadata.AppData.JsonContent == commentFile.AppData.JsonContent);
        Assert.IsTrue(updatedCommentFile.FileMetadata.GlobalTransitId == originalCommentUploadResult.GlobalTransitId);

        Assert.IsTrue(updatedCommentFile.FileMetadata.Created == originalCommentFile.FileMetadata.Created);
        Assert.IsTrue(updatedCommentFile.FileMetadata.Updated > originalCommentFile.FileMetadata.Updated);
        Assert.IsTrue(updatedCommentFile.FileMetadata.AppData.JsonContent == updatedComment);
        Assert.IsTrue(updatedCommentFile.FileMetadata.GlobalTransitId == originalCommentFile.FileMetadata.GlobalTransitId);

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    private async Task<UploadResult> UploadStandardFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, int fileType)
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
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
    }

    private async Task<UploadResult> OverwriteStandardFile(OwnerApiClient client, ExternalFileIdentifier overwriteFile, string uploadedContent, int fileType)
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
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.OwnerOnly
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, overwriteFile.TargetDrive, fileMetadata, overwriteFileId: overwriteFile.FileId);
    }
}