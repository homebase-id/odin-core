using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Serialization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Core.Storage;
using Odin.Core.Time;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.DataSubscription;

[TestFixture]
public class DataSubscriptionAndDistributionTests1
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
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill; I think";
        var firstUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        // Sam should have the same content on his feed drive since it was distributed by the backend
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { fileType }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Batch size should be 1 but was {batch.SearchResults.Count()}");
        var originalFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(originalFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(originalFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(originalFile.FileMetadata.GlobalTransitId == firstUploadResult.GlobalTransitId);

        //Now change the file as if someone edited a post
        var updatedContent = "No really, I'm Frodo Baggins";
        var uploadResult2 = await OverwriteStandardFile(
            client: frodoOwnerClient,
            overwriteFile: firstUploadResult.File,
            updatedContent,
            fileType,
            versionTag: firstUploadResult.NewVersionTag);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        //Sam should have changes; note - we're using the same query params intentionally
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsTrue(batch2.SearchResults.Count() == 1, $"Count should be 1 but was {batch2.SearchResults.Count()}");
        var updatedFile = batch2.SearchResults.First();
        ClassicAssert.IsTrue(updatedFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(updatedFile.FileMetadata.Created == originalFile.FileMetadata.Created);
        ClassicAssert.IsTrue(updatedFile.FileMetadata.Updated > originalFile.FileMetadata.Updated);
        ClassicAssert.IsTrue(updatedFile.FileMetadata.AppData.Content == updatedContent);
        ClassicAssert.IsTrue(updatedFile.FileMetadata.AppData.Content != originalFile.FileMetadata.AppData.Content);
        ClassicAssert.IsTrue(updatedFile.FileMetadata.GlobalTransitId == originalFile.FileMetadata.GlobalTransitId);
        ClassicAssert.IsTrue(updatedFile.FileMetadata.ReactionPreview == null, "ReactionPreview should be null on initial file upload; even tho it was updated");

        // ClassicAssert.IsTrue(updatedFile.FileMetadata.ReactionPreview.TotalCommentCount == originalFile.FileMetadata.ReactionPreview.TotalCommentCount);
        // CollectionAssert.AreEquivalent(updatedFile.FileMetadata.ReactionPreview.Reactions, originalFile.FileMetadata.ReactionPreview.Reactions);
        // CollectionAssert.AreEquivalent(updatedFile.FileMetadata.ReactionPreview.Comments, originalFile.FileMetadata.ReactionPreview.Comments);

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

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
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1);
        var originalFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(originalFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(originalFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(originalFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Frodo now deletes the file
        await frodoOwnerClient.Drive.DeleteFile(standardFileUploadResult.File);

        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp2 = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        //Sam should have the file marked as deleted
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp2);
        ClassicAssert.IsTrue(batch2.SearchResults.Count() == 1);
        var deletedFile = batch2.SearchResults.First();
        ClassicAssert.IsTrue(deletedFile.FileState == FileState.Deleted, "File should be deleted");
        ClassicAssert.IsTrue(deletedFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        //Tell frodo's identity to process the outbox due to feed distribution
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);
        
        // Sam should have the same content on his feed drive
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { standardFileType }
        };

        // Sam should have the blog post
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "Are you tho?" }),
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
        ClassicAssert.IsTrue(!commentBatch.SearchResults.Any());

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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

        var securedChannelCircle = await frodoOwnerClient.Membership.CreateCircle("Secured channel content", new PermissionSetGrantRequest()
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
        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { securedChannelCircle.Id });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });


        // Sam to follow everything from frodo
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Expected 1 but count was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have sam comment on the file
        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "Are you tho?" }),
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
        ClassicAssert.IsTrue(!commentBatch.SearchResults.Any());

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        ClassicAssert.IsTrue(theFile2.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile2.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        ClassicAssert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.Content == commentFile.AppData.Content));

        //All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);

        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);
        
        //TODO: should sam have to process transit instructions for feed items?
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have sam comment on the file
        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "Are you tho?" }),
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
        ClassicAssert.IsTrue(!commentBatch.SearchResults.Any());

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        //Tell frodo's identity to process the outbox due to feed distribution
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        ClassicAssert.IsTrue(theFile2.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile2.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        ClassicAssert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.Content == commentFile.AppData.Content));

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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

        var securedChannelCircle = await frodoOwnerClient.Membership.CreateCircle("Secured channel content", new PermissionSetGrantRequest()
        {
            Drives = new List<DriveGrantRequest>()
            {
                new()
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
        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { securedChannelCircle.Id });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var standardFileUploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var standardFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { standardFileUploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        // Sam should have the blog post from frodo in Sam's feed
        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1);
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have Sam comment on the file
        var commentFileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "Are you tho?" }),
                FileType = commentFileType,
                DataType = 202,
                UserDate = UnixTimeUtc.ZeroTime,
                Tags = default
            },
            AccessControlList = AccessControlList.Anonymous
        };

        // transfer a comment from Sam directly to frodo
        var transitResult = await samOwnerClient.Transit.TransferFileHeader(
            commentFileMetadata,
            recipients: new List<string>() { frodoOwnerClient.Identity.OdinId },
            remoteTargetDrive: frodoChannelDrive,
            overwriteGlobalTransitFileId: null,
            thumbnail: null,
            fileSystemType: FileSystemType.Comment
        );

        //comment should have made it directly to the recipient's server
        ClassicAssert.IsTrue(transitResult.RecipientStatus.Count == 1);
        var s = transitResult.RecipientStatus[frodoOwnerClient.Identity.OdinId];
        ClassicAssert.IsTrue(s == TransferStatus.Enqueued, $"Status should be DeliveredToTargetDrive but was {s}");

        // weird
        await samOwnerClient.Transit.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };


        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        ClassicAssert.IsTrue(!commentBatch.SearchResults.Any());

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        //
        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        ClassicAssert.IsTrue(theFile2.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile2.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview, "Reaction Preview is null");
        ClassicAssert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.Content == commentFileMetadata.AppData.Content));
        //TODO: test the other file parts here


        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);

        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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

        var securedChannelCircle = await frodoOwnerClient.Membership.CreateCircle("Secured channel content", new PermissionSetGrantRequest()
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
        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { securedChannelCircle.Id });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (standardFileUploadResult, encryptedStandardFileJsonContent64, _) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, standardFileType);

        //Tell frodo's identity to process the outbox
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

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
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == encryptedStandardFileJsonContent64);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);

        //Now, have Sam comment on the file
        var commentFile = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = true,
            ReferencedFile = standardFileUploadResult.GlobalTransitIdFileIdentifier,
            AppData = new()
            {
                Content = OdinSystemSerializer.Serialize(new { message = "Are you tho?" }),
                FileType = commentFileType,
                DataType = 202,
                UserDate = UnixTimeUtc.ZeroTime,
                Tags = default
            },
            AccessControlList = AccessControlList.Connected
        };

        // transfer a comment from Sam directly to frodo
        var (transitResult, _) = await samOwnerClient.Transit.TransferEncryptedFileHeader(
            FileSystemType.Comment,
            commentFile,
            recipients: [frodoOwnerClient.Identity.OdinId],
            remoteTargetDrive: frodoChannelDrive,
            overwriteGlobalTransitFileId: null,
            thumbnail: null
        );

        //comment should have made it directly to the recipient's server
        ClassicAssert.IsTrue(transitResult.RecipientStatus.Count == 1);
        var s = transitResult.RecipientStatus[frodoOwnerClient.Identity.OdinId];
        ClassicAssert.IsTrue(s == TransferStatus.Enqueued, $"Status should be DeliveredToTargetDrive but was {s}");

        // weird
        await samOwnerClient.Transit.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        var commentFileQueryParams = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            FileType = new List<int>() { commentFileType }
        };

        //
        // Sam should not have the comment since they are not distributed
        //
        var commentBatch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Comment, commentFileQueryParams);
        ClassicAssert.IsTrue(!commentBatch.SearchResults.Any());

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        // Sam should, however, have a reaction summary update for that comment on the original file
        //
        var batch2 = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, standardFileQueryParams);
        ClassicAssert.IsTrue(batch2.SearchResults.Count() == 1);
        var theFile2 = batch2.SearchResults.First();
        ClassicAssert.IsTrue(theFile2.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile2.FileMetadata.AppData.Content == encryptedStandardFileJsonContent64);
        ClassicAssert.IsTrue(theFile2.FileMetadata.GlobalTransitId == standardFileUploadResult.GlobalTransitId);
        ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview, "Reaction Preview is null");
        ClassicAssert.IsTrue(theFile2.FileMetadata.ReactionPreview.TotalCommentCount == 1);
        ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.IsEncrypted));
        ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.Content == ""));
        // ClassicAssert.IsNotNull(theFile2.FileMetadata.ReactionPreview.Comments.SingleOrDefault(c => c.JsonContent == commentFile.AppData.JsonContent));
        //TODO: test the other file parts here


        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);

        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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

        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);
        
        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        Guid? uniqueId = Guid.NewGuid();
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType, uniqueId);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        //It should be direct write
        // Sam should have the same content on his feed drive
        // await samOwnerClient.Transit.ProcessIncomingInstructionSet(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == uploadedContent);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.UniqueId == null, "feed uniqueId should be null");

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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

        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64, _) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType, Guid.NewGuid());

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);
        
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == encryptedJsonContent64);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task EncryptedStandardFile_UploadedByOwner_Distributed_ToFollowers_That_AreNotConnected_ReceivesNoFiles()
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
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64, _) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        //Process the outbox since we're sending an encrypted file
        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsTrue(!batch.SearchResults.Any(), $"Count should be 0 but was {batch.SearchResults.Count()}");

        //All done

        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
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
        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //Pippin and merry follow a channel
        await pippinOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.SelectedChannels,
            new List<TargetDrive>() { frodoChannelDrive });
        await merryOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.SelectedChannels,
            new List<TargetDrive>() { frodoChannelDrive });


        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

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
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);

        await pippinOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await merryOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task
        UnencryptedStandardFile_UploadedByOwner_DistributeTo_Both_ConnectedAndUnconnected_Followers_And_DeletedFrom_FollowersFeeds_When_Owner_Deletes_File()
    {
        const int fileType = 1117;

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
        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        //Pippin and merry follow a channel
        await pippinOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.SelectedChannels,
            new List<TargetDrive>() { frodoChannelDrive });
        await merryOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.SelectedChannels,
            new List<TargetDrive>() { frodoChannelDrive });


        // Frodo uploads content to channel drive
        const string uploadedContent = "I'm Mr. Underhill";
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

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


        //
        // The Frodo deletes the file
        //
        await frodoOwnerClient.Drive.DeleteFile(uploadResult.File);

        // Validate Frodo no longer has it
        var getDeletedFileResponse = await frodoOwnerClient.Drive.GetFileHeaderRaw(FileSystemType.Standard, uploadResult.File);
        ClassicAssert.IsTrue(getDeletedFileResponse.IsSuccessStatusCode);
        ClassicAssert.IsTrue(getDeletedFileResponse.Content.FileState == FileState.Deleted, "frodo's file should be marked deleted");

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        //
        // Sam's feed drive no longer has the header
        // 
        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDrive_HasDeletedFile(samOwnerClient, uploadResult);

        //
        // Pippin's feed drive no longer has the header
        // 
        await pippinOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDrive_HasDeletedFile(pippinOwnerClient, uploadResult);

        //
        // Merry's feed drive no longer has the header
        // 
        await merryOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);
        await AssertFeedDrive_HasDeletedFile(merryOwnerClient, uploadResult);


        //All done
        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);

        await pippinOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
        await merryOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    public async Task CommentingOn_EncryptedStandardFile_Updates_ReactionPreview()
    {
        const int fileType = 11345;

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

        await frodoOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { });
        await samOwnerClient.Network.AcceptConnectionRequest(frodoOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.OwnerFollower.FollowIdentity(frodoOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // Frodo uploads content to channel drive
        var uploadedContent = "I'm Mr. Underhill";
        var (uploadResult, encryptedJsonContent64, _) =
            await UploadStandardEncryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

        await frodoOwnerClient.Transit.WaitForEmptyOutbox(frodoChannelDrive);

        await samOwnerClient.Transit.ProcessInbox(SystemDriveConstants.FeedDrive);

        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            // FileType = new List<int>() { fileType }
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await samOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == encryptedJsonContent64);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

        //All done

        await frodoOwnerClient.Network.DisconnectFrom(samOwnerClient.Identity);
        await samOwnerClient.Network.DisconnectFrom(frodoOwnerClient.Identity);
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    private async Task AssertFeedDrive_HasDeletedFile(OwnerApiClient client, UploadResult uploadResult)
    {
        var qp = new FileQueryParams()
        {
            TargetDrive = SystemDriveConstants.FeedDrive,
            GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitId.GetValueOrDefault() }
        };

        var batch = await client.Drive.QueryBatch(FileSystemType.Standard, qp);
        ClassicAssert.IsNotNull(batch.SearchResults.SingleOrDefault(c => c.FileState == FileState.Deleted));
    }

    private async Task AssertFeedDriveHasFile(OwnerApiClient client, FileQueryParams queryParams, string expectedContent, UploadResult expectedUploadResult)
    {
        var batch = await client.Drive.QueryBatch(FileSystemType.Standard, queryParams);
        ClassicAssert.IsTrue(batch.SearchResults.Count() == 1, $"Count should be 1 but was {batch.SearchResults.Count()}");
        var theFile = batch.SearchResults.First();
        ClassicAssert.IsTrue(theFile.FileState == FileState.Active);
        ClassicAssert.IsTrue(theFile.FileMetadata.AppData.Content == expectedContent);
        ClassicAssert.IsTrue(theFile.FileMetadata.GlobalTransitId == expectedUploadResult.GlobalTransitId);
    }

    private async Task<UploadResult> UploadStandardUnencryptedFileToChannel(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent,
        int fileType, Guid? uniqueId = null)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = fileType,
                GroupId = default,
                UniqueId = uniqueId,
                Tags = default
            },
            AccessControlList = AccessControlList.Anonymous
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
    }

    private async Task<(UploadResult uploadResult, string encryptedJsonContent64, string encryptedPayloadContent64)>
        UploadStandardEncryptedFileToChannel(
            OwnerApiClient client,
            TargetDrive targetDrive,
            string uploadedContent,
            int fileType,
            Guid? uniqueId = null)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default,
                UniqueId = uniqueId
            },
            AccessControlList = AccessControlList.Connected
        };

        var uploadResponse = await client.DriveRedux.UploadNewEncryptedMetadata(targetDrive, fileMetadata);
        var uploadResult = uploadResponse.response.Content;
        return (uploadResult, uploadResponse.encryptedJsonContent64, "");
    }

    private async Task<UploadResult> OverwriteStandardFile(OwnerApiClient client, ExternalFileIdentifier overwriteFile, string uploadedContent, int fileType,
        Guid versionTag)
    {
        var fileMetadata = new UploadFileMetadata()
        {
            AllowDistribution = true,
            IsEncrypted = false,
            VersionTag = versionTag,
            AppData = new()
            {
                Content = uploadedContent,
                FileType = fileType,
                GroupId = default,
                Tags = default
            },
            AccessControlList = AccessControlList.Anonymous
        };

        return await client.Drive.UploadFile(FileSystemType.Standard, overwriteFile.TargetDrive, fileMetadata, overwriteFileId: overwriteFile.FileId);
    }
}