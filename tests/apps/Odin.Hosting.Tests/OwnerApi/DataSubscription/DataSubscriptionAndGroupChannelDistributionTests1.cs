using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription;

namespace Odin.Hosting.Tests.OwnerApi.DataSubscription;

[TestFixture]
public class DataSubscriptionAndGroupChannelDistributionTests1
{
    private WebScaffold _scaffold;

    private static readonly Dictionary<string, string> IsGroupChannelAttributes = new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } };

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
    [Ignore("return to these after prototyping phase")]
    public async Task GroupChannelMember_CanUpdateStandardFileAndDistributeChangesForAllNotifications()
    {
        const int fileType = 2001;
        var groupIdentityOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Frodo);
        var groupIdentityOwnerClientRedux = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var samOwnerClient = _scaffold.CreateOwnerApiClient(TestIdentities.Samwise);

        //create a channel drive
        var groupChannelDrive = new TargetDrive()
        {
            Alias = Guid.NewGuid(),
            Type = SystemDriveConstants.ChannelDriveType
        };

        await groupIdentityOwnerClient.Drive.CreateDrive(groupChannelDrive, "A Group Channel Drive", "", allowAnonymousReads: false, ownerOnly: false,
            allowSubscriptions: true, attributes: IsGroupChannelAttributes);

        var memberCircleId = Guid.NewGuid();
        ;
        await groupIdentityOwnerClientRedux.Network.CreateCircle(memberCircleId, "group members", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new()
                        {
                            Drive = groupChannelDrive,
                            Permission = DrivePermission.ReadWrite
                        },
                    }
                },
                PermissionSet = default
            }
        );

        await groupIdentityOwnerClient.Network.SendConnectionRequestTo(samOwnerClient.Identity, new List<GuidId>() { memberCircleId });
        await samOwnerClient.Network.AcceptConnectionRequest(groupIdentityOwnerClient.Identity, new List<GuidId>() { });

        // Sam to follow everything from frodo
        await samOwnerClient.OwnerFollower.FollowIdentity(groupIdentityOwnerClient.Identity, FollowerNotificationType.AllNotifications, null);

        // channel member posts they are here
        var uploadedContent = "Hi all, I'm here; my name is Sam.";
        var firstUploadResult = await UploadStandardUnencryptedFileToChannel(samOwnerClient, groupChannelDrive, uploadedContent, fileType);
        
        await groupIdentityOwnerClient.Transit.WaitForEmptyOutbox(groupChannelDrive);


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
            client: groupIdentityOwnerClient,
            overwriteFile: firstUploadResult.File,
            updatedContent,
            fileType,
            versionTag: firstUploadResult.NewVersionTag);

        await groupIdentityOwnerClient.Transit.WaitForEmptyOutbox(groupChannelDrive);
        
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
        await samOwnerClient.OwnerFollower.UnfollowIdentity(groupIdentityOwnerClient.Identity);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
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
    [Ignore("return to these after prototyping phase")]
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
        var uploadResult = await UploadStandardUnencryptedFileToChannel(frodoOwnerClient, frodoChannelDrive, uploadedContent, fileType);

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

        //All done
        await samOwnerClient.OwnerFollower.UnfollowIdentity(frodoOwnerClient.Identity);
    }

    [Test]
    [Ignore("return to these after prototyping phase")]
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
    [Ignore("return to these after prototyping phase")]
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
    [Ignore("return to these after prototyping phase")]
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

        //Process the outbox since we're sending an encrypted file
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
    [Ignore("return to these after prototyping phase")]
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
    [Ignore("return to these after prototyping phase")]
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
        int fileType)
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
            int fileType)
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
                Tags = default
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