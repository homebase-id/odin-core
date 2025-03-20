using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.DataSubscription.Follower;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Hosting.Tests._Universal.DriveTests;

// Covers using the drives directly on the identity (i.e owner console, app, and Guest endpoints)
// Does not test security but rather drive features
public class DirectDriveSizeValidationTests
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

    public static IEnumerable OwnerAllowed()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable AppAllowed()
    {
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    public static IEnumerable WhenGuestOnlyHasReadAccess()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task FailWithBadRequestWhenAppDataContentIsTooLarge(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());
        await callerContext.Initialize(ownerApiClient);

        // Act
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task FailWithBadRequestWhenPreviewThumbnailIsTooLarge(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100);
        uploadedFileMetadata.AppData.Content = "123";
        uploadedFileMetadata.AppData.PreviewThumbnail = new ThumbnailContent
        {
            PixelWidth = 100,
            PixelHeight = 100,
            ContentType = "image/png",
            Content = Enumerable.Repeat((byte)'A', ThumbnailContent.MaxTinyThumbLength + 1).ToArray()
        };
        
        await callerContext.Initialize(ownerApiClient);

        // Act
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await callerDriveClient.UploadNewMetadata(targetDrive, uploadedFileMetadata);

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.BadRequest);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    public async Task CanUpdateEncryptedMetadataWithStorageIntent_MetadataOnly(IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true);

        const string originalContent = "some content here";
        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AppData.Content = originalContent;

        var originalKeyHeader = KeyHeader.NewRandom16();
        var (response, _) = await ownerApiClient.DriveRedux
            .UploadNewEncryptedMetadata(targetDrive, uploadedFileMetadata, originalKeyHeader);
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        // Act

        var uploadResult = response.Content;
        var getHeaderResponse1 = await ownerApiClient.DriveRedux.GetFileHeader(uploadResult.File);
        ClassicAssert.IsTrue(getHeaderResponse1.IsSuccessStatusCode);
        var uploadedFile1 = getHeaderResponse1.Content;

        await callerContext.Initialize(ownerApiClient);
        var callerDriveClient = new UniversalDriveApiClient(identity.OdinId, callerContext.GetFactory());
        
        var updatedMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        updatedMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());;
        updatedMetadata.VersionTag = uploadedFile1.FileMetadata.VersionTag;
        updatedMetadata.IsEncrypted = true;

        var newKeyHeader = new KeyHeader()
        {
            Iv = ByteArrayUtil.GetRndByteArray(16),
            AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
        };

        var (updateResponse, _) = await callerDriveClient
            .UpdateExistingEncryptedMetadata(uploadResult.File, newKeyHeader, updatedMetadata);

        ClassicAssert.IsTrue(updateResponse.StatusCode == HttpStatusCode.BadRequest);
    }


    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    public async Task FailWithBadRequestWithIdentityOtherThanOriginalAuthorUpdatesAFileUsingUpdateBatch(
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
            attributes: new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } });

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
        ClassicAssert.IsTrue(response.IsSuccessStatusCode);

        // wait for the collab channel to distribute feed
        await collabChannelOwnerClient.DriveRedux.ProcessInbox(collabChannelDrive);
        await collabChannelOwnerClient.DriveRedux.WaitForEmptyOutbox(collabChannelDrive);

        //
        // Update the file via pippin's identity
        //

        await originalAuthor_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await secondaryAuthor_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);
        await member2_OwnerClient.DriveRedux.ProcessInbox(SystemDriveConstants.FeedDrive);

        var remoteTargetFile = response.Content.RemoteGlobalTransitIdFileIdentifier.ToFileIdentifier();
        await callerContext.Initialize(originalAuthor_OwnerClient);
        var callerDriveClient = new UniversalDriveApiClient(originalAuthor, callerContext.GetFactory());

        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = new string(Enumerable.Repeat('A', AppFileMetaData.MaxAppDataContentLength + 1).ToArray());
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
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == HttpStatusCode.BadRequest);

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