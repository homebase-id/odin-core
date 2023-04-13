using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.DataSubscription.Follower;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.DataSubscription;

[TestFixture]
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
        var firstUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Tell frodo's identity to process the outbox
        // await frodoOwnerClient.Transit.ProcessOutbox(1);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        // Sam should have the same content on his feed drive since it was distributed by the backend
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { fileType }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Batch size should be 1 but was {batch.SearchResults.Count()}");
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

        //Tell frodo's identity to process the outbox due to feed distribution
        await frodoOwnerClient.Transit.ProcessOutbox(1);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

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
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

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
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

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
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        //Tell frodo's identity to process the outbox due to feed distribution
        await frodoOwnerClient.Transit.ProcessOutbox(1);

        //Tell Frodo's identity to process the feed outbox
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

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

        // Upload a comment by the owner
        var originalCommentUploadResult = await frodoOwnerClient.Drive.UploadFile(FileSystemType.Comment, frodoChannelDrive, commentFile, "");

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        // Sam should not have the comment since they are not distributed
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(!commentBatch.SearchResults.Any());

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAddedByOwnerToStandardUnencryptedFile_WhenConnected()
    {
        const int standardFileType = 1121;
        const int commentFileType = 383;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, ownerOnly: false, allowSubscriptions: true);

        var securedChannelCircle = await frodoOwnerClient.Network.CreateCircle("Secured channel content", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = frodoChannelDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        //
        // Connect sam and frodo; sam gets access to the secured channel
        //
        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { securedChannelCircle.Id });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });


        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        // await frodoOwnerClient.Transit.ProcessOutbox(1);
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Expected 1 but count was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have sam comment on the file
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

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(!commentBatch.SearchResults.Any());

        await frodoOwnerClient.Cron.DistributeFeedFiles();


        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        Assert.IsTrue(theFile2.FileState == FileState.Active);
        Assert.IsTrue(theFile2.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        Assert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.JsonContent == commentFile.AppData.JsonContent));

        //All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);

        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAddedByOwnerToStandardUnencryptedFile_NotConnected()
    {
        const int standardFileType = 1121;
        const int commentFileType = 383;

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
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        // await frodoOwnerClient.Cron.ProcessTransitOutbox(1);
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        //TODO: should sam have to process transit instructions for feed items?
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have sam comment on the file
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

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);


        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(!commentBatch.SearchResults.Any());

        // force frodo's identity to process the feed to distribute the post
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        //Tell frodo's identity to process the outbox due to feed distribution
        await frodoOwnerClient.Transit.ProcessOutbox(1);

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        Assert.IsTrue(theFile2.FileState == FileState.Active);
        Assert.IsTrue(theFile2.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        Assert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.JsonContent == commentFile.AppData.JsonContent));

        //All done
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAdded_ByAnother_ConnectedIdentity_ToStandardUnencryptedFile()
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

        var securedChannelCircle = await frodoOwnerClient.Network.CreateCircle("Secured channel content", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = frodoChannelDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        //
        // Connect sam and frodo; sam gets access to the secured channel
        //
        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { securedChannelCircle.Id });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        //Tell frodo's identity to process the outbox
        await frodoOwnerClient.Transit.ProcessOutbox(1);

        //TODO: should sam have to process transit instructions for feed items?
        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        await frodoOwnerClient.Cron.DistributeFeedFiles();
        // Sam should have the blog post from frodo in Sam's feed
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have Sam comment on the file
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

        // transfer a comment from Sam directly to frodo
        var transitResult = await samOwnerClient.Transit.TransferFile(
            FileSystemType.Comment,
            commentFile,
            recipients: new List<string>() { frodoOwnerClient.Identity.OdinId },
            remoteTargetDrive: frodoChannelDrive,
            payloadData: "",
            overwriteGlobalTransitFileId: null,
            thumbnail: null
        );

        //comment should have made it directly to the recipient's server
        Assert.IsTrue(transitResult.RecipientStatus.Count == 1);
        var s = transitResult.RecipientStatus[frodoOwnerClient.Identity.OdinId];
        Assert.IsTrue(s == TransferStatus.DeliveredToTargetDrive, $"Status should be DeliveredToTargetDrive but was {s}");

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        // await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(!commentBatch.SearchResults.Any());

        await frodoOwnerClient.Cron.DistributeFeedFiles();

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        Assert.IsTrue(theFile2.FileState == FileState.Active);
        Assert.IsTrue(theFile2.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview, "Reaction Preview is null");
        Assert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.JsonContent == commentFile.AppData.JsonContent));
        //TODO: test the other file parts here


        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);

        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task ReactionSummaryIsDistributedWhenCommentAdded_ByAnotherConnectedIdentity_ToStandardEncryptedFile_AndJsonContentIsEmpty()
    {
        const int standardFileType = 9441;
        const int commentFileType = 9999;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", false, ownerOnly: false, allowSubscriptions: true);

        var securedChannelCircle = await frodoOwnerClient.Network.CreateCircle("Secured channel content", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new DriveGrantRequest()
                {
                    PermissionedDrive = new PermissionedDrive()
                    {
                        Drive = frodoChannelDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            }
        });

        //
        // Connect sam and frodo; sam gets access to the secured channel
        //
        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { securedChannelCircle.Id });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (standardFileUploadResult, encryptedStandardFileJsonContent64) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        //Tell frodo's identity to process the outbox
        await frodoOwnerClient.Transit.ProcessOutbox(1);

        //TODO: should sam have to process transit instructions for feed items?
        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == encryptedStandardFileJsonContent64);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have Sam comment on the file
        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            ContentType = "application/json",
            PayloadIsEncrypted = true,
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

        // transfer a comment from Sam directly to frodo
        var (transitResult, encryptedJsonContent64) = await samOwnerClient.Transit.TransferEncryptedFile(
            FileSystemType.Comment,
            commentFile,
            recipients: new List<string>() { frodoOwnerClient.Identity.OdinId },
            remoteTargetDrive: frodoChannelDrive,
            payloadData: "",
            overwriteGlobalTransitFileId: null,
            thumbnail: null
        );

        //comment should have made it directly to the recipient's server
        Assert.IsTrue(transitResult.RecipientStatus.Count == 1);
        var s = transitResult.RecipientStatus[frodoOwnerClient.Identity.OdinId];
        Assert.IsTrue(s == TransferStatus.DeliveredToTargetDrive, $"Status should be DeliveredToTargetDrive but was {s}");

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        // await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        Assert.IsTrue(!commentBatch.SearchResults.Any());

        // force process the feed to distribute the updated reaction preview
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        Assert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        Assert.IsTrue(theFile2.FileState == FileState.Active);
        Assert.IsTrue(theFile2.FileMetadata.AppData.JsonContent == encryptedStandardFileJsonContent64);
        Assert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview, "Reaction Preview is null");
        Assert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.IsEncrypted));
        Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.JsonContent == ""));
        // Assert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.JsonContent == commentFile.AppData.JsonContent));
        //TODO: test the other file parts here


        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);

        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task UnencryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 10144;

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

        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Cron.DistributeFeedFiles();

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == uploadedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task UnencryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected()
    {
        const int fileType = 10144;

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
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Tell Frodo's identity to process the feed outbox
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        //It should be direct write
        // Sam should have the same content on his feed drive
        // await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
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
    public async Task EncryptedStandardFile_UploadedByOwner_DistributedToConnectedIdentities_ThatFollowOwner()
    {
        const int fileType = 11344;

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

        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) = await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.ProcessOutbox(1);


        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == encryptedJsonContent64);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task EncryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected_Fails()
    {
        const int fileType = 11355;

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
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64) = await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.ProcessOutbox(1);

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        Assert.IsTrue(!batch.SearchResults.Any(), $"Count should be 0 but was {batch.SearchResults.Count()}");

        //All done

        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task UnencryptedStandardFile_UploadedByOwner_DistributeTo_Both_ConnectedAndUnconnected_Followers()
    {
        const int fileType = 1111;

        var frodoOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);
        var merryOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Merry);
        var pippinOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Pippin);

        //create a channel drive
        var frodoChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await frodoOwnerClient.Drive.CreateDrive(frodoChannelDrive, "A Channel Drive", "", allowAnonymousReads: true, ownerOnly: false,
            allowSubscriptions: true);

        // Sam is connected to follow everything from frodo
        await frodoOwnerClient.Network.SendConnectionRequest(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //Pippin and merry follow a channel
        await pippinOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.SelectedChannels,
            new List<TargetDrive>() { frodoChannelDrive });
        await merryOwnerClient.Follower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.SelectedChannels,
            new List<TargetDrive>() { frodoChannelDrive });


        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Cron.DistributeFeedFiles();

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await pippinOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await merryOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };
        //All should have the file
        await AssertFeedDriveHasFile(samOwnerClient, qp, uploadedContent, uploadResult);
        await AssertFeedDriveHasFile(pippinOwnerClient, qp, uploadedContent, uploadResult);
        await AssertFeedDriveHasFile(merryOwnerClient, qp, uploadedContent, uploadResult);
        //All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);

        await pippinOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
        await merryOwnerClient.Follower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    private async Task AssertFeedDriveHasFile(OwnerApiClient client, FileQueryParams queryParams, string expectedContent, UploadResult expectedUploadResult)
    {
        var batch = await client.Drive.QueryBatch(FileSystemType.Standard, queryParams);
        Assert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        Assert.IsTrue(theFile.FileState == FileState.Active);
        Assert.IsTrue(theFile.FileMetadata.AppData.JsonContent == expectedContent);
        Assert.IsTrue(theFile.FileMetadata.GlobalTransitId == expectedUploadResult.GlobalTransitId);
    }

    private async Task<UploadResult> UploadStandardUnencryptedFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent,
        int fileType)
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
            AccessControlList = AccessControlList.Authenticated
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
    }

    private async Task<(UploadResult uploadResult, string encryptedJsonContent64)> UploadStandardEncryptedFileToChannel(OwnerApiClient client,
        TargetDrive targetDrive, string uploadedContent,
        int fileType)
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
            AccessControlList = AccessControlList.Connected
        };

        return await client.Drive.UploadEncryptedFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
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