using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Mediator;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Workers.DefaultCron;
using Youverse.Core.Services.Workers.FeedDistributionApp;
using Youverse.Core.Storage;
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
        const int FileType = 10133;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        // await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, FileType);

        // var j = new FeedDistributionJob();
        // var svc = SystemHttpClient.CreateHttps<IFeedDistributionClient>(frodoOwnerClient.Identity.OdinId);
        // var distributeFeedItemResponse = await svc.DistributeQueuedItems();
        // Assert.IsTrue(distributeFeedItemResponse.IsSuccessStatusCode);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        
        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { FileType }
            // GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
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

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill; I think";
        var firstUploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        // Sam should have the same content on his feed drive since it was distributed by the backend
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
        Assert.IsTrue(originalFile.FileMetadata.GlobalTransitId == firstUploadResult.GlobalTransitId);

        //Now change the file as if someone edited a post
        var updatedContent = "No really, I'm Frodo Baggins";
        var uploadResult2 = await OverwriteStandardFile(
            client: frodoOwnerClient,
            overwriteFile: firstUploadResult.File,
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
        Assert.IsTrue(updatedFile.FileMetadata.ReactionPreview == null, "ReactionPreview should be null on initial file upload; even tho it was updated");

        // Assert.IsTrue(updatedFile.FileMetadata.ReactionPreview.TotalCommentCount == originalFile.FileMetadata.ReactionPreview.TotalCommentCount);
        // CollectionAssert.AreEquivalent(updatedFile.FileMetadata.ReactionPreview.Reactions, originalFile.FileMetadata.ReactionPreview.Reactions);
        // CollectionAssert.AreEquivalent(updatedFile.FileMetadata.ReactionPreview.Comments, originalFile.FileMetadata.ReactionPreview.Comments);

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    [Ignore("causes other tests to fail when multiple tests are running, need ot figure out w/ Michael")]
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

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true);

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

        var qp2 = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        //Sam should have the file marked as deleted
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp2);
        Assert.IsTrue(batch2.SearchResults.Count() == 1);
        var deletedFile = batch2.SearchResults.First();
        Assert.IsTrue(deletedFile.FileState == FileState.Deleted, "File should be deleted");
        Assert.IsTrue(deletedFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task CommentsAreNotDistributed()
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
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(new { message = "Are you tho?" }),
                FileType = commentFileType,
                DataType = 202,
                UserDate = UnixTimeUtc.ZeroTime,
                Tags = default
            }
        };

        // Upload a comment
        var originalCommentUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile, "");

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        // Sam should not have the comment since they are not distributed
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(!commentBatch.SearchResults.Any());

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    [Description("Tests that a reaction summary is sent to allow followers when a comment is added")]
    public async Task ReactionSummaryIsDistributedWhenCommentAdded()
    {
        const int standardFileType = 441;
        const int commentFileType = 9989;

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
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                ContentIsComplete = true,
                JsonContent = DotYouSystemSerializer.Serialize(new { message = "Are you tho?" }),
                FileType = commentFileType,
                DataType = 202,
                UserDate = UnixTimeUtc.ZeroTime,
                Tags = default
            }
        };

        // Upload a comment from frodo
        var originalCommentUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile, "");

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);


        // Sam should not have the comment since they are not distributed
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(!commentBatch.SearchResults.Any());

        // Sam should, however, have a reaction summary update for that comment on the original file

        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        Assert.IsTrue(theFile2.FileState == FileState.Active);
        Assert.IsTrue(theFile2.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        Assert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.JsonContent == commentFile.AppData.JsonContent));
        //TODO: test the other file parts here

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