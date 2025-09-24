using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Core.Cryptography;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.Permissions;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._Universal.Peer.DirectSend;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class PeerUpdateFileTests
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
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Pippin, TestIdentities.Samwise });
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
    public async Task CanUpdateRemoteFileFileUpdateHeaderDeletePayloadAndAddNewPayload_PeerOnly(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

        var sender = senderOwnerClient.Identity.OdinId;
        var recipient = recipientOwnerClient.Identity.OdinId;

        var remoteTargetDrive = TargetDrive.NewTargetDrive();
        await recipientOwnerClient.DriveManager.CreateDrive(remoteTargetDrive, "Test Drive 001", "", allowAnonymousReads: true,
            allowSubscriptions: true,
            attributes: IsCollaborativeChannelAttributes);

        var cid = Guid.NewGuid();
        var permissions = TestUtils.CreatePermissionGrantRequest(remoteTargetDrive, DrivePermission.Write);
        await recipientOwnerClient.Network.CreateCircle(cid, "circle with some access", permissions);

        await senderOwnerClient.Connections.SendConnectionRequest(recipient);
        await recipientOwnerClient.Connections.AcceptConnectionRequest(sender, [cid]);

        // upload metadata
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 555;
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
        var response = await senderOwnerClient.PeerDirect.TransferNewFile(remoteTargetDrive, uploadedFileMetadata, [recipient], null,
            uploadManifest,
            testPayloads);
        await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        await recipientOwnerClient.DriveRedux.ProcessInbox(remoteTargetDrive, Int32.MaxValue);
        await recipientOwnerClient.DriveRedux.WaitForEmptyInbox(remoteTargetDrive);

        //
        // Update the file via pippin's identity
        //

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 777;

        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Peer,

            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = remoteTargetFile,
            Recipients = [recipient],
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

        await callerContext.Initialize(senderOwnerClient);
        var callerDriveClient = new UniversalDriveApiClient(sender, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd]);
        await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        await recipientOwnerClient.DriveRedux.ProcessInbox(remoteTargetDrive, Int32.MaxValue);
        await recipientOwnerClient.DriveRedux.WaitForEmptyInbox(remoteTargetDrive);

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = updateFileResponse.Content;
            ClassicAssert.IsNotNull(uploadResult);

            // await recipientOwnerClient.DriveRedux.WaitForEmptyInbox(remoteTargetFile.TargetDrive);

            //
            // Recipient should have the updated file
            //
            var getHeaderResponse =
                await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 2);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.All(pd => pd.Key != payload1.Key), "payload 1 should have been removed");
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payload2.Key), "payload 2 should remain");
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
                "payloadToAdd should have been, well, added :)");

            var recipientFile = new ExternalFileIdentifier()
            {
                FileId = header.FileId,
                TargetDrive = header.TargetDrive
            };

            //
            // Ensure payloadToAdd add is added
            //
            var getPayloadToAddResponse = await recipientOwnerClient.DriveRedux.GetPayload(recipientFile, payloadToAdd.Key);
            ClassicAssert.IsTrue(getPayloadToAddResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(getPayloadToAddResponse.ContentHeaders!.LastModified.HasValue);
            ClassicAssert.IsTrue(getPayloadToAddResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

            var content = (await getPayloadToAddResponse.Content.ReadAsStreamAsync()).ToByteArray();
            CollectionAssert.AreEqual(content, payloadToAdd.Content);

            // Check all the thumbnails
            foreach (var thumbnail in payloadToAdd.Thumbnails)
            {
                var getThumbnailResponse = await recipientOwnerClient.DriveRedux.GetThumbnail(recipientFile,
                    thumbnail.PixelWidth, thumbnail.PixelHeight, payloadToAdd.Key);

                ClassicAssert.IsTrue(getThumbnailResponse.IsSuccessStatusCode);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders!.LastModified.HasValue);
                ClassicAssert.IsTrue(getThumbnailResponse.ContentHeaders.LastModified.GetValueOrDefault() < DateTimeOffset.Now.AddSeconds(10));

                var thumbContent = (await getThumbnailResponse.Content.ReadAsStreamAsync()).ToByteArray();
                CollectionAssert.AreEqual(thumbContent, thumbnail.Content);
            }

            //
            // Ensure we get payload2 for the payload1
            //
            var getPayload2Response = await recipientOwnerClient.DriveRedux.GetPayload(recipientFile, payload2.Key);
            ClassicAssert.IsTrue(getPayload2Response.IsSuccessStatusCode);

            //
            // Ensure we get 404 for the payload1
            //
            var getPayload1Response = await recipientOwnerClient.DriveRedux.GetPayload(recipientFile, payload1.Key);
            ClassicAssert.IsTrue(getPayload1Response.StatusCode == HttpStatusCode.NotFound);

            //
            // Ensure we find the file on the recipient
            // 
            var searchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = recipientFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == recipientFile.FileId);
        }

        await senderOwnerClient.Connections.DisconnectFrom(recipient);
        await recipientOwnerClient.Connections.DisconnectFrom(sender);
        await callerContext.Cleanup();
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    // [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    // [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateRemoteEncryptedFile_AndSeeChangesDistributedToFeed(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var member1_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var collabChannelOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
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
        uploadedFileMetadata.AppData.Content = "some content here";
        uploadedFileMetadata.AllowDistribution = true;
        uploadedFileMetadata.AppData.DataType = 1234;
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
        var (response, firstEncryptedMetadataContent64) = await member1_OwnerClient.PeerDirect.TransferNewEncryptedFile(collabChannelDrive,
            uploadedFileMetadata, [collabChannel], null, uploadManifest,
            testPayloads, keyHeader: keyHeader);
        await member1_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        //
        // Update the file via pippin's identity
        //

        await member1_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        await callerContext.Initialize(member1_OwnerClient);
        var callerDriveClient = new UniversalDriveApiClient(member1, callerContext.GetFactory());

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here";
        updatedFileMetadata.AppData.DataType = 5678;

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
        var (updateFileResponse, updatedEncryptedMetadataContent64, _, _) =
            await callerDriveClient.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);
        await member1_OwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromMinutes(30));
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = updateFileResponse.Content;
            ClassicAssert.IsNotNull(uploadResult);

            await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive); //waiting for distribution to occur

            // file should be on the feed of those connected
            var globalTransitIdFileIdentifier = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = remoteTargetFile.GlobalTransitId.GetValueOrDefault(),
                TargetDrive = SystemDriveConstants.FeedDrive
            };

            await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
            await member2_OwnerClient.DriveRedux.WaitForEmptyInbox(SystemDriveConstants.FeedDrive);

            var channelOnMembersFeedDrive = await member2_OwnerClient.DriveRedux.QueryByGlobalTransitId(globalTransitIdFileIdentifier);
            ClassicAssert.IsTrue(channelOnMembersFeedDrive.IsSuccessStatusCode);
            var theFileOnFeedDrive = channelOnMembersFeedDrive.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileOnFeedDrive);

            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.Content == updatedEncryptedMetadataContent64);
            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.SenderOdinId == collabChannel,
                $"sender was {theFileOnFeedDrive.FileMetadata.SenderOdinId}");
            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.OriginalAuthor == member1,
                $"original author was {theFileOnFeedDrive.FileMetadata.OriginalAuthor}");
        }

        await callerContext.Cleanup();

        ClassicAssert.IsTrue((await member1_OwnerClient.Connections.DisconnectFrom(collabChannel)).IsSuccessStatusCode);
        ClassicAssert.IsTrue((await member2_OwnerClient.Connections.DisconnectFrom(collabChannel)).IsSuccessStatusCode);

        ClassicAssert.IsTrue((await collabChannelOwnerClient.Connections.DisconnectFrom(member1)).IsSuccessStatusCode);
        ClassicAssert.IsTrue((await collabChannelOwnerClient.Connections.DisconnectFrom(member2)).IsSuccessStatusCode);

        ClassicAssert.IsTrue((await member1_OwnerClient.Follower.UnfollowIdentity(collabChannel)).IsSuccessStatusCode);
        ClassicAssert.IsTrue((await member2_OwnerClient.Follower.UnfollowIdentity(collabChannel)).IsSuccessStatusCode);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    // [TestCaseSource(nameof(AppWithOnlyUseTransitWrite))]
    // [TestCaseSource(nameof(GuestNotAllowed))]
    public async Task CanUpdateRemoteFile_AndSeeChangesDistributedToFeed(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var member1_OwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);
        var collabChannelOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
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
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        // the collab channel we get the file from the TransferNewFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive); //waiting for distribution to occur

        //
        // Update the file via pippin's identity
        //

        await member1_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

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
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // the collab channel we get the file from the UpdateFile and we need to wait for it to send it out to all followers
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive); //waiting for distribution to occur

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var uploadResult = updateFileResponse.Content;
            ClassicAssert.IsNotNull(uploadResult);

            // handle any incoming feed items
            await collabChannelOwnerClient.DriveRedux.WaitForEmptyInbox(remoteTargetFile.TargetDrive);

            //
            // Recipient should have the updated file
            //
            var getHeaderResponse =
                await collabChannelOwnerClient.DriveRedux.QueryByGlobalTransitId(remoteTargetFile.ToGlobalTransitIdFileIdentifier());
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Count() == 2);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.All(pd => pd.Key != payload1.Key), "payload 1 should have been removed");
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payload2.Key), "payload 2 should remain");
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
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
            ClassicAssert.IsTrue(channelOnMembersFeedDrive.IsSuccessStatusCode);
            var theFileOnFeedDrive = channelOnMembersFeedDrive.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileOnFeedDrive);

            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.SenderOdinId == collabChannel,
                $"sender was {theFileOnFeedDrive.FileMetadata.SenderOdinId}");
            ClassicAssert.IsTrue(theFileOnFeedDrive.FileMetadata.OriginalAuthor == member1,
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
}