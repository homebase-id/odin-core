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
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._Universal.Peer.DirectSend;

public class PeerUpdateOriginalAuthorTests
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
    // [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    // [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateRemoteEncryptedFile_FromIdentityOtherThanOriginalAuthor_AndSeeChangesDistributedToFeed(
        IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var debugTimeSpan = TimeSpan.FromMinutes(30);

        var collabChannelOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Collab);
        var originalAuthor_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var secondaryAuthor_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Merry);
        var member2_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

        await collabChannelOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await originalAuthor_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await member2_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await secondaryAuthor_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        var originalAuthor = originalAuthor_OwnerClient.OdinId;
        var collabChannel = collabChannelOwnerClient.OdinId;
        var secondaryAuthor = secondaryAuthor_OwnerClient.OdinId;
        var member2 = member2_OwnerClient.OdinId;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwnerClient.DriveManager.CreateDrive(collabChannelDrive, "Test channel drive 001", "", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        //
        // get everyone connected and in a circle for the collab channel
        //
        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwnerClient.Network.CreateCircle(collabChannelId, "circle with some access", permissions);


        Assert.IsTrue((await originalAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel)).IsSuccessStatusCode);
        Assert.IsTrue((await collabChannelOwnerClient.Connections.AcceptConnectionRequest(originalAuthor, [collabChannelId]))
            .IsSuccessStatusCode);

        Assert.IsTrue((await secondaryAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel)).IsSuccessStatusCode);
        Assert.IsTrue((await collabChannelOwnerClient.Connections.AcceptConnectionRequest(secondaryAuthor, [collabChannelId]))
            .IsSuccessStatusCode);

        Assert.IsTrue((await member2_OwnerClient.Connections.SendConnectionRequest(collabChannel)).IsSuccessStatusCode);
        Assert.IsTrue((await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member2, [collabChannelId])).IsSuccessStatusCode);

        await member2_OwnerClient.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

        //
        // original author makes a post (upload metadata)
        //
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.DataType = 333;
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AppData.FileType = 100;
        uploadedFileMetadata.AllowDistribution = true;
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

        var keyHeader = KeyHeader.NewRandom16();

        //Pippin sends a file to the recipient
        var (originalFileUpload, _) = await originalAuthor_OwnerClient.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            uploadedFileMetadata, [collabChannel], null, uploadManifest,
            testPayloads, keyHeader: keyHeader);
        await originalAuthor_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        Assert.IsTrue(originalFileUpload.IsSuccessStatusCode);

        await collabChannelOwnerClient.DriveRedux.ProcessInbox(collabChannelDrive);
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyInbox(collabChannelDrive);

        // When the collab channel gets the file, we need to wait for feed distribution to occur
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive);

        //
        // Update the file via pippin's identity
        //

        await originalAuthor_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await secondaryAuthor_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var remoteTargetFile = originalFileUpload.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        var globalTransitIdFileIdentifierOnFeed = new GlobalTransitIdFileIdentifier()
        {
            GlobalTransitId = remoteTargetFile.GlobalTransitId.GetValueOrDefault(),
            TargetDrive = SystemDriveConstants.FeedDrive
        };

        //
        // validate member2 got the file before we update it
        //

        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.FeedDrive, debugTimeSpan);
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeSpan);

        await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await member2_OwnerClient.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.FeedDrive);

        var member2FileOnFeedBeforeUpdateResponse =
            await member2_OwnerClient.DriveRedux.QueryByGlobalTransitId(globalTransitIdFileIdentifierOnFeed);
        Assert.IsTrue(member2FileOnFeedBeforeUpdateResponse.IsSuccessStatusCode);
        var theFileOnFeedDriveBeforeUpdate = member2FileOnFeedBeforeUpdateResponse.Content.SearchResults.SingleOrDefault();
        Assert.IsNotNull(theFileOnFeedDriveBeforeUpdate);

        //
        //
        //


        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 444;
        updatedFileMetadata.AppData.UniqueId = Guid.Parse("00000000-0000-0000-0000-111111111111");

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
        await callerContext.Initialize(secondaryAuthor_OwnerClient);
        var callerContextDriveClient = new UniversalDriveApiClient(secondaryAuthor, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedMetadataContent64, _, _) = await callerContextDriveClient.UpdateEncryptedFile(
            updateInstructionSet,
            updatedFileMetadata,
            [payloadToAdd],
            keyHeader);

        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");
        await secondaryAuthor_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeSpan);
        await secondaryAuthor_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.FeedDrive, debugTimeSpan);

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = updateFileResponse.Content;
            Assert.IsNotNull(uploadResult);

            await collabChannelOwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
            await collabChannelOwnerClient.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.FeedDrive, debugTimeSpan);
            //waiting for distribution to occur
            await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.FeedDrive, debugTimeSpan);
            await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, debugTimeSpan);

            await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
            await member2_OwnerClient.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.FeedDrive);

            var tempTempDriveStatus = await member2_OwnerClient.DriveRedux.GetDriveStatus(SystemDriveConstants.TransientTempDrive);
            Assert.IsNotNull(tempTempDriveStatus.Content);
            Assert.IsTrue(tempTempDriveStatus.Content.Outbox.TotalItems == 0);
            Assert.IsTrue(tempTempDriveStatus.Content.Inbox.TotalItems == 0);

            var feedDriveStatus = await member2_OwnerClient.DriveRedux.GetDriveStatus(SystemDriveConstants.FeedDrive);
            Assert.IsNotNull(feedDriveStatus.Content);
            Assert.IsTrue(feedDriveStatus.Content.Outbox.TotalItems == 0);
            Assert.IsTrue(feedDriveStatus.Content.Inbox.TotalItems == 0);

            // var xr = await member2_OwnerClient.DriveRedux.QueryBatch(new QueryBatchRequest
            // {
            //     QueryParams = new()
            //     {
            //         TargetDrive = SystemDriveConstants.FeedDrive,
            //     },
            //     ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            // });

            var channelOnMembersFeedDrive =
                await member2_OwnerClient.DriveRedux.QueryByGlobalTransitId(globalTransitIdFileIdentifierOnFeed);
            Assert.IsTrue(channelOnMembersFeedDrive.IsSuccessStatusCode);
            var theFileOnFeedDrive = channelOnMembersFeedDrive.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileOnFeedDrive);

            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.Content == updatedEncryptedMetadataContent64);
            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.SenderOdinId == collabChannel,
                $"sender was {theFileOnFeedDrive.FileMetadata.SenderOdinId}");
            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.OriginalAuthor == originalAuthor,
                $"original author was {theFileOnFeedDrive.FileMetadata.SenderOdinId}");
        }

        await originalAuthor_OwnerClient.Connections.DisconnectFrom(collabChannel);
        await secondaryAuthor_OwnerClient.Connections.DisconnectFrom(collabChannel);
        await member2_OwnerClient.Connections.DisconnectFrom(collabChannel);

        await collabChannelOwnerClient.Connections.DisconnectFrom(originalAuthor);
        await collabChannelOwnerClient.Connections.DisconnectFrom(secondaryAuthor);
        await collabChannelOwnerClient.Connections.DisconnectFrom(member2);

        await originalAuthor_OwnerClient.Follower.UnfollowIdentity(collabChannel);
        await secondaryAuthor_OwnerClient.Follower.UnfollowIdentity(collabChannel);
        await member2_OwnerClient.Follower.UnfollowIdentity(collabChannel);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    // [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    // [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateRemoteFile_FromIdentityOtherThanOriginalAuthor_AndSeeChangesDistributedToFeed(
        IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var secondaryAuthor_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Collab);

        var originalAuthor_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var collabChannelOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var member2_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.TomBombadil);

        await collabChannelOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await originalAuthor_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await member2_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);
        await secondaryAuthor_OwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

        var originalAuthor = originalAuthor_OwnerClient.OdinId;
        var collabChannel = collabChannelOwnerClient.OdinId;
        var secondaryAuthor = secondaryAuthor_OwnerClient.OdinId;
        var member2 = member2_OwnerClient.OdinId;

        var collabChannelDrive = TargetDrive.NewTargetDrive(SystemDriveConstants.ChannelDriveType);
        await collabChannelOwnerClient.DriveManager.CreateDrive(collabChannelDrive, "Test channel drive 001", "", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var collabChannelId = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(collabChannelDrive, DrivePermission.Write);
        await collabChannelOwnerClient.Network.CreateCircle(collabChannelId, "circle with some access", permissions);

        await originalAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(originalAuthor, [collabChannelId]);

        await secondaryAuthor_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(secondaryAuthor, [collabChannelId]);

        await member2_OwnerClient.Connections.SendConnectionRequest(collabChannel);
        await collabChannelOwnerClient.Connections.AcceptConnectionRequest(member2, [collabChannelId]);
        await member2_OwnerClient.Follower.FollowIdentity(collabChannel, FollowerNotificationType.AllNotifications);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.DataType = 111;
        uploadedFileMetadata.AllowDistribution = true;
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
        var response = await originalAuthor_OwnerClient.PeerDirect.TransferNewFile(collabChannelDrive, uploadedFileMetadata,
            [collabChannel], null,
            uploadManifest,
            testPayloads);
        await originalAuthor_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
        Assert.IsTrue(response.IsSuccessStatusCode);

        // wait for the collab channel to distribute feed
        await collabChannelOwnerClient.DriveRedux.ProcessInbox(collabChannelDrive, Int32.MaxValue);
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive);

        //
        // Update the file via pippin's identity
        //

        await originalAuthor_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive, Int32.MaxValue);
        await secondaryAuthor_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive, Int32.MaxValue);
        await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive, Int32.MaxValue);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        await callerContext.Initialize(originalAuthor_OwnerClient);
        var callerDriveClient = new UniversalDriveApiClient(originalAuthor, callerContext.GetFactory());

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 222;

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
        await originalAuthor_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

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
            Assert.IsTrue(theFileOnFeedDrive.FileMetadata.OriginalAuthor == originalAuthor,
                $"sender was {theFileOnFeedDrive.FileMetadata.SenderOdinId}");
        }

        await originalAuthor_OwnerClient.Connections.DisconnectFrom(collabChannel);
        await secondaryAuthor_OwnerClient.Connections.DisconnectFrom(collabChannel);
        await member2_OwnerClient.Connections.DisconnectFrom(collabChannel);

        await collabChannelOwnerClient.Connections.DisconnectFrom(originalAuthor);
        await collabChannelOwnerClient.Connections.DisconnectFrom(secondaryAuthor);
        await collabChannelOwnerClient.Connections.DisconnectFrom(member2);

        await originalAuthor_OwnerClient.Follower.UnfollowIdentity(collabChannel);
        await secondaryAuthor_OwnerClient.Follower.UnfollowIdentity(collabChannel);
        await member2_OwnerClient.Follower.UnfollowIdentity(collabChannel);
    }
}