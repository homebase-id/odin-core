using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Identity;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drive.Core.Query;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives.Base.Upload;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.Upload;
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

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, false);

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
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
    public async Task CanUploadCommentFileToDriveAndDistributeToFollower()
    {
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
        var standardFileUploadResult = await UploadStandardFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent);

        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = false,
            ReferencedFile = standardFileUploadResult.File,
            AppData = new()
            {
                ContentIsComplete = false,
                JsonContent = DotYouSystemSerializer.Serialize(new { message = "a reply comment" }),
                FileType = 909,
                DataType = 202,
                UserDate = 0,
                Tags = default
            }
        };

        var commentFileUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile, "some payload data");

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { 200 }
        };

        // Sma should have the blog post
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        // Sma should have the comment
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
        Assert.IsTrue(commentBatch.SearchResults.Count() == 1);
        var theCommentFile = commentBatch.SearchResults.First();
        Assert.IsTrue(theCommentFile.FileState == FileState.Active);
        Assert.IsTrue(theCommentFile.FileMetadata.AppData.JsonContent == commentFile.AppData.JsonContent);
        Assert.IsTrue(theCommentFile.FileMetadata.GlobalTransitId == commentFileUploadResult.GlobalTransitId);


        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
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