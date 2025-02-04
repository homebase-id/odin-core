using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Drives.FileSystem.Base.Upload.Attachments;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests._Universal.Concepts.CollaborationChannel;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class CollaborationChannelTests
{
    private WebScaffold _scaffold;

    private static readonly Dictionary<string, string> IsCollaborativeChannelAttributes = new()
        { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } };

    // Other Tests
    // Bad Requests - fail when missing payload operation type, invalid upload manifest


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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppWithOnlyUseTransitWrite()
    {
        yield return new object[]
            { new AppPermissionKeysOnly(new TestPermissionKeyList(PermissionKeys.UseTransitWrite)), HttpStatusCode.OK };
    }

    public static IEnumerable GuestNotAllowed()
    {
        yield return new object[]
        {
            new ConnectedIdentityLoggedInOnGuestApi(TestIdentities.Pippin.OdinId, new TestPermissionKeyList(PermissionKeys.ReadWhoIFollow)),
            HttpStatusCode.MethodNotAllowed
        };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanViewAndEditCollaborativePostFromFeed(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var collabChannel = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Collab);
        var member1 = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var member2 = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);

        await SetupScenario(collabChannel, member1, member2, collabChannelDrive);

        var keyHeader = KeyHeader.NewRandom16();

        var (response, firstFileUploadMetadata, payload1) =
            await AwaitPostNewEncryptedFileOverPeerDirect(member1, collabChannelDrive, collabChannel, keyHeader);
        Assert.IsTrue(response.IsSuccessStatusCode);

        // The collab channel gets the file then will redistribute to its followers' feeds
        await collabChannel.DriveRedux.WaitForFeedOutboxDistribution(collabChannelDrive);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        // 
        // Assert member1 and member2 have the files in their feed
        //
        await AssertHasFileInFeed(member1, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), firstFileUploadMetadata);
        await AssertHasFileInFeed(member2, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), firstFileUploadMetadata);

        //
        // Update the file from Pippin's feed app (then wait for the outbox to process)
        //
        var (updateFileResponse, updatedFileMetadata, updatedEncryptedMetadataContent64) =
            await AwaitUpdateFile(callerContext, member2, firstFileUploadMetadata, remoteTargetFile, collabChannel, payload1, keyHeader);
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            //
            // Assert collab channel has the updated file
            //
            var updatedFileInCollabChannelResponse =
                await collabChannel.DriveRedux.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
            var updatedFileInCollabChannel = updatedFileInCollabChannelResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsTrue(updatedFileInCollabChannel!.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(updatedFileInCollabChannel!.FileMetadata.OriginalAuthor == member1.OdinId);

            //
            // The collab channel gets the file then will redistribute to its followers' feeds
            //
            await collabChannel.DriveRedux.WaitForFeedOutboxDistribution(collabChannelDrive);

            var uploadResult = updateFileResponse.Content;
            Assert.IsNotNull(uploadResult);


            //
            // Assert: member1 and member 2 have the updated file in their feed
            //


            await AssertHasFileInFeed(member1, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), updatedFileMetadata);
            await AssertHasFileInFeed(member2, remoteTargetFile.GlobalTransitId.GetValueOrDefault(), updatedFileMetadata);
        }

        await callerContext.Cleanup();
        await CleanupScenario(collabChannel, member1, member2);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateRemoteFile_AndSeeChangesDistributedToFeed(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var collabChannelOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Collab);
        var member1_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var member2_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await collabChannelOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await member1_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await member2_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        var member1 = member1_OwnerClient.OdinId;
        var collabChannel = collabChannelOwnerClient.OdinId;
        var member2 = member2_OwnerClient.OdinId;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwnerClient.DriveManager.CreateDrive(collabChannelDrive, "Test channel drive 001", "", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwnerClient.Network.CreateCircle(collabChannelId, "circle with some access", permissions);

        await member1_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member1, [collabChannelId]);

        await member2_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member2, [collabChannelId]);
        await member2_OwnerClient.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 888;
        uploadedFileMetadata.AccessControlList = AccessControlList.Connected;
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        //Pippin sends a file to the recipient
        var response = await member1_OwnerClient.PeerDirect.TransferNewFile(collabChannelDrive, uploadedFileMetadata, [collabChannel], null,
            uploadManifest,
            testPayloads);
        await member1_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        Assert.IsTrue(response.IsSuccessStatusCode);

        // the collab channel we get the file from the TransferNewFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive); //waiting for distribution to occur

        //
        // Update the file via pippin's identity
        //

        await member1_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive, Int32.MaxValue);
        await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive, Int32.MaxValue);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        await callerContext.Initialize(member1_OwnerClient);
        var callerDriveClient = new UniversalDriveApiClient(member1, callerContext.GetFactory());

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 999;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteTargetFile,
            Recipients = [collabChannel],
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = Guid.Empty.ToByteArray(),
                        PayloadKey = payloadToAdd.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                    },
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payload1.Key
                    }
                ]
            }
        };

        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        await member1_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // the collab channel we get the file from the UpdateFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive); //waiting for distribution to occur

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = updateFileResponse.Content;
            Assert.IsNotNull(uploadResult);

            // handle any incoming feed items
            await collabChannelOwnerClient.DriveRedux.WaitForEmptyInbox(remoteTargetFile.TargetDrive);

            //
            // Recipient should have the updated file
            //
            var getHeaderResponse =
                await collabChannelOwnerClient.DriveRedux.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            Assert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(header.FileMetadata.Payloads.Count() == 2);
            Assert.IsTrue(header.FileMetadata.Payloads.All(pd => pd.Key != payload1.Key), "payload 1 should have been removed");
            Assert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payload2.Key), "payload 2 should remain");
            Assert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
                "payloadToAdd should have been, well, added :)");

            // file should be on the feed of those connected
            var globalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = header.FileMetadata.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = SystemDriveConstants.FeedDrive
            };

            await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive); //waiting for distribution to occur
            await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive, int.MaxValue);
            await member2_OwnerClient.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.FeedDrive);

            var channelOnMembersFeedDrive = await member2_OwnerClient.DriveRedux.QueryByGlobalTransitId(globalTransitIdFileIdentifier);
            Assert.IsTrue(channelOnMembersFeedDrive.IsSuccessStatusCode);
            var theFileOnFeedDrive = channelOnMembersFeedDrive.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileOnFeedDrive);

            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.SenderOdinId == collabChannel,
                $"sender was {theFileOnFeedDrive.FileMetadata.SenderOdinId}");
            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.OriginalAuthor == member1,
                $"original author was {theFileOnFeedDrive.FileMetadata.OriginalAuthor}");
        }

        await callerContext.Cleanup();

        await member1_OwnerClient.Connections.DisconnectFrom(collabChannel);
        await member2_OwnerClient.Connections.DisconnectFrom(collabChannel);

        await collabChannelOwnerClient.Connections.DisconnectFrom(member1);
        await collabChannelOwnerClient.Connections.DisconnectFrom(member2);

        await member1_OwnerClient.Follower.UnfollowIdentity(collabChannel);
        await member2_OwnerClient.Follower.UnfollowIdentity(collabChannel);
    }


    private static async Task<(ApiResponse<UploadPayloadResult> updateFileResponse, UploadFileMetadata updatedFile, string
            updatedEncryptedMetadataContent64)>
        AwaitUpdateFile(IApiClientContext callerContext, OwnerApiClientRedux sender, UploadFileMetadata uploadedFileMetadata,
            FileIdentifier remoteTargetFile,
            OwnerApiClientRedux collabChannel, TestPayloadDefinition payload1, KeyHeader keyHeader)
    {
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 5678;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteTargetFile,
            Recipients = [collabChannel.OdinId],
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.AppendOrOverwrite,
                        Iv = ByteArrayUtil.GetRndByteArray(16),
                        PayloadKey = payloadToAdd.Key,
                        DescriptorContent = null,
                        ContentType = payloadToAdd.ContentType,
                        PreviewThumbnail = default,
                        Thumbnails = new List<UploadedManifestThumbnailDescriptor>(),
                    },
                    new UploadManifestPayloadDescriptor()
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = payload1.Key
                    }
                ]
            }
        };

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);
        await callerContext.Initialize(sender);
        var callerDriveClient = new UniversalDriveApiClient(sender.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedMetadataContent64, uploadedPayloads, uploadedThumbnails) =
            await callerDriveClient.UpdateEncryptedFile(
                updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);

        if (updateFileResponse.IsSuccessStatusCode)
        {
            await callerDriveClient.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        }

        return (updateFileResponse, updatedFileMetadata, updatedEncryptedMetadataContent64);
    }

    private static async Task AssertHasFileInFeed(OwnerApiClientRedux recipient, Guid globalTransitId,
        UploadFileMetadata expectedFileMetadata)
    {
        await recipient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive, Int32.MaxValue);
        await recipient.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.FeedDrive);
        var fileOnFeed = new FileIdentifier()
        {
            GlobalTransitId = globalTransitId,
            TargetDrive = SystemDriveConstants.FeedDrive
        };

        var getFileByGtidResponse = await recipient.DriveRedux.QueryByGlobalTransitId(fileOnFeed.ToGlobalTransitIdFileIdentifier());
        var theFile = getFileByGtidResponse.Content.SearchResults.SingleOrDefault();

        Assert.IsNotNull(theFile, $"{recipient.OdinId} is missing file in the feed");
        Assert.IsTrue(theFile.FileMetadata.AppData.DataType == expectedFileMetadata.AppData.DataType);
        // Assert.IsTrue(theFile.FileMetadata.AppData.Content == updatedEncryptedMetadataContent64);
        // Assert.IsTrue(theFile.FileMetadata.SenderOdinId == collabChannel, $"sender was {theFileOnFeedDrive.FileMetadata.SenderOdinId}");
        // Assert.IsTrue(theFile.FileMetadata.OriginalAuthor == member1, $"original author was {theFileOnFeedDrive.FileMetadata.OriginalAuthor}");
    }

    private static async Task<(ApiResponse<TransitResult> response, UploadFileMetadata uploadedMetadata, TestPayloadDefinition payload1)>
        AwaitPostNewEncryptedFileOverPeerDirect(
            OwnerApiClientRedux sender, TargetDrive collabChannelDrive,
            OwnerApiClientRedux collabChannel,
            KeyHeader keyHeader)
    {
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 7779;
        uploadedFileMetadata.AccessControlList = AccessControlList.Connected;
        var payload1 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();

        payload1.Iv = ByteArrayUtil.GetRndByteArray(16);
        var payload2 = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail2();
        payload2.Iv = ByteArrayUtil.GetRndByteArray(16);

        var testPayloads = new List<TestPayloadDefinition>()
        {
            payload1,
            payload2
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        ApiResponse<TransitResult> response = null;

        // for (var i = 0; i < 100; i++)
        {
            //Pippin sends a file to the recipient
            (response, _) = await sender.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
                uploadedFileMetadata, [collabChannel.OdinId], null, uploadManifest,
                testPayloads, keyHeader: keyHeader);
        }

        await sender.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

        return (response, uploadedFileMetadata, payload1);
    }

    private async Task SetupScenario(OwnerApiClientRedux collabChannel, OwnerApiClientRedux member1, OwnerApiClientRedux member2,
        TargetDrive collabChannelDrive)
    {
        await collabChannel.Configuration.DisableAutoAcceptIntroductions(true);
        await member1.Configuration.DisableAutoAcceptIntroductions(true);
        await member2.Configuration.DisableAutoAcceptIntroductions(true);

        await collabChannel.DriveManager.CreateDrive(collabChannelDrive, "Test channel drive 001", "",
            allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannel.Network.CreateCircle(collabChannelId, "circle with some access", permissions);

        await member1.Connections.SendConnectionRequest(collabChannel.OdinId);
        await collabChannel.Connections.AcceptConnectionRequest(member1.OdinId, [collabChannelId]);

        await member2.Connections.SendConnectionRequest(collabChannel.OdinId);
        await collabChannel.Connections.AcceptConnectionRequest(member2.OdinId, [collabChannelId]);

        await member1.Follower.FollowIdentity(collabChannel.OdinId, FollowerNotificationType.AllNotifications);
        await member2.Follower.FollowIdentity(collabChannel.OdinId, FollowerNotificationType.AllNotifications);
    }

    private async Task CleanupScenario(OwnerApiClientRedux collabChannel, OwnerApiClientRedux member1, OwnerApiClientRedux member2)
    {
        await collabChannel.Connections.DisconnectFrom(member1.OdinId);
        await collabChannel.Connections.DisconnectFrom(member2.OdinId);
        await member1.Connections.DisconnectFrom(collabChannel.OdinId);
        await member2.Connections.DisconnectFrom(collabChannel.OdinId);

        Assert.IsTrue((await member1.Follower.UnfollowIdentity(collabChannel.OdinId)).IsSuccessStatusCode,
            "member1 failed to unfollow collab channel");
        Assert.IsTrue((await member2.Follower.UnfollowIdentity(collabChannel.OdinId)).IsSuccessStatusCode,
            "member2 failed to unfollow collab channel");
    }
}