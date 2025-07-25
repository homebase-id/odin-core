using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Update;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Peer.UpdateBatch;

public class UpdateBatchWithRecipientsRemoteUpsertEncrypted
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
        yield return new object[]
        {
            new AppReadWriteAccessToDrive(TargetDrive.NewTargetDrive(), new TestPermissionKeyList(PermissionKeys.UseTransitWrite)),
            HttpStatusCode.OK
        };
    }

    public static IEnumerable GuestHasWriteAccess()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    public static IEnumerable WhenGuestOnlyHasReadAccess()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServer_Encrypted_WithAppNotifications(
        IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(sender);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true,
            attributes: new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } });

        //
        // Setup - upload a new file with payloads 
        // 

        List<TestIdentity> recipients = [TestIdentities.Frodo, TestIdentities.Merry];

        await SetupRecipients(sender, recipients, targetDrive);

        var keyHeader = KeyHeader.NewRandom16();

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        const string originalUploadedContent = "some content here..";
        uploadedFileMetadata.AppData.Content = originalUploadedContent;

        // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server
        var transitOptions = new TransitOptions { };

        var storageOptions = new StorageOptions()
        {
            Drive = targetDrive,
        };

        var (uploadNewFileResponse, encryptedJsonContent64) = await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(
            uploadedFileMetadata,
            storageOptions,
            transitOptions,
            keyHeader);

        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;


        // verify setup

        var getHeaderToVerifyResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderToVerifyResponse.IsSuccessStatusCode);
        var headerToVerify = getHeaderToVerifyResponse.Content;
        ClassicAssert.IsNotNull(headerToVerify);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.IsEncrypted);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.AppData.Content == encryptedJsonContent64);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.AppData.DataType == uploadedFileMetadata.AppData.DataType);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.VersionTag == uploadResult.NewVersionTag);
        ClassicAssert.IsFalse(headerToVerify.FileMetadata.Payloads.Any());

        var localSS = ownerApiClient.GetTokenContext().SharedSecret;
        var localKeyHeaderToVerify = headerToVerify.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref localSS);
        var localDecryptedBytes = localKeyHeaderToVerify.Decrypt(headerToVerify.FileMetadata.AppData.Content.FromBase64());
        ClassicAssert.IsTrue(localDecryptedBytes.ToStringFromUtf8Bytes() == originalUploadedContent);

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        const string updatedContentToBeDistributed = "some new content to be distributed";
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = updatedContentToBeDistributed;
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var r = recipients.Select(r => r.OdinId).ToList();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = r,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            },
            UseAppNotification = true,
            AppNotificationOptions = new()
            {
                AppId = Guid.NewGuid(),
                TypeId = Guid.NewGuid(),
                TagId = Guid.NewGuid(),
                Silent = false,
                PeerSubscriptionId = Guid.NewGuid(),
                Recipients = r,
                UnEncryptedMessage = "test message here"
            }
        };

        await callerContext.Initialize(ownerApiClient);

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);

        var callerDriveClient = new UniversalDriveApiClient(sender.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedContent64, _, _) =
            await callerDriveClient.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [], keyHeader);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            ClassicAssert.IsNotNull(updateFileResponse.Content);
            await callerDriveClient.WaitForEmptyOutbox(targetDrive);

            //
            // ensure the local file exists and is updated correctly
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.IsEncrypted);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedEncryptedContent64);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(header.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
            ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any());

            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);

            // ensure the recipients get the file

            foreach (var recipient in recipients)
            {
                var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);
                var recipientFileResponse =
                    await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
                var remoteFileHeader = recipientFileResponse.Content.SearchResults.FirstOrDefault();

                ClassicAssert.IsNotNull(remoteFileHeader);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.IsEncrypted);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.AppData.Content == updatedEncryptedContent64); //latest update
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
                ClassicAssert.IsFalse(remoteFileHeader.FileMetadata.Payloads.Any());

                // validate we can decrypt it on the recipient
                // remoteFileHeader.FileMetadata.AppData.Content

                var sharedSecret = recipientOwnerClient.GetTokenContext().SharedSecret;
                var remoteKeyHeader = remoteFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(keyHeader.Iv, remoteKeyHeader.Iv));
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(keyHeader.AesKey.GetKey(), remoteKeyHeader.AesKey.GetKey()));
                var decryptedBytes = remoteKeyHeader.Decrypt(remoteFileHeader.FileMetadata.AppData.Content.FromBase64());
                ClassicAssert.IsTrue(decryptedBytes.ToStringFromUtf8Bytes() == updatedContentToBeDistributed);


                // valid recipient got the notification
                var allNotificationsResponse = await recipientOwnerClient.AppNotifications.GetList(100);
                ClassicAssert.IsTrue(allNotificationsResponse.IsSuccessStatusCode);
                var notifications = allNotificationsResponse.Content;
                ClassicAssert.IsNotNull(notifications);

                ClassicAssert.IsNotNull(notifications.Results.SingleOrDefault(
                    n => n.SenderId == ownerApiClient.OdinId
                         && n.Options.TypeId == updateInstructionSet.AppNotificationOptions.TypeId
                         && n.Options.PeerSubscriptionId == updateInstructionSet.AppNotificationOptions.PeerSubscriptionId
                         && n.Options.UnEncryptedMessage == updateInstructionSet.AppNotificationOptions.UnEncryptedMessage
                         && n.Options.TagId == updateInstructionSet.AppNotificationOptions.TagId));
            }
        }

        await Disconnect(sender, recipients, targetDrive);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestHasWriteAccess))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task
        CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServer_SomeRecipientsHaveFile_SomeDoNotHaveFile_Encrypted(
            IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(sender);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true,
            attributes: new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } });

        //
        // Setup - upload a new file with payloads 
        // 

        List<TestIdentity> recipientsWithFile = [TestIdentities.Frodo, TestIdentities.Merry];
        List<TestIdentity> recipientsWithoutFile = [TestIdentities.Samwise, TestIdentities.TomBombadil];

        var allRecipients = recipientsWithFile.Concat(recipientsWithoutFile).ToList();
        await SetupRecipients(sender, allRecipients, targetDrive);

        var keyHeader = KeyHeader.NewRandom16();

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        const string originalUploadedContent = "some content here..";
        uploadedFileMetadata.AppData.Content = originalUploadedContent;

        // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server
        var transitOptions = new TransitOptions
        {
            Recipients = recipientsWithFile.Select(r => r.OdinId.DomainName).ToList()
        };

        var storageOptions = new StorageOptions()
        {
            Drive = targetDrive,
        };

        var (uploadNewFileResponse, encryptedJsonContent64) = await ownerApiClient.DriveRedux.UploadNewEncryptedMetadata(
            uploadedFileMetadata,
            storageOptions,
            transitOptions,
            keyHeader);

        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;


        // verify setup

        var getHeaderToVerifyResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderToVerifyResponse.IsSuccessStatusCode);
        var headerToVerify = getHeaderToVerifyResponse.Content;
        ClassicAssert.IsNotNull(headerToVerify);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.IsEncrypted);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.AppData.Content == encryptedJsonContent64);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.AppData.DataType == uploadedFileMetadata.AppData.DataType);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.VersionTag == uploadResult.NewVersionTag);
        ClassicAssert.IsFalse(headerToVerify.FileMetadata.Payloads.Any());

        var localSS = ownerApiClient.GetTokenContext().SharedSecret;
        var localKeyHeaderToVerify = headerToVerify.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref localSS);
        var localDecryptedBytes = localKeyHeaderToVerify.Decrypt(headerToVerify.FileMetadata.AppData.Content.FromBase64());
        ClassicAssert.IsTrue(localDecryptedBytes.ToStringFromUtf8Bytes() == originalUploadedContent);

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        const string updatedContentToBeDistributed = "some new content to be distributed";
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = updatedContentToBeDistributed;
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var r = allRecipients.Select(r => r.OdinId).ToList();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = r,
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            },
            UseAppNotification = true,
            AppNotificationOptions = new()
            {
                AppId = Guid.NewGuid(),
                TypeId = Guid.NewGuid(),
                TagId = Guid.NewGuid(),
                Silent = false,
                PeerSubscriptionId = Guid.NewGuid(),
                Recipients = r,
                UnEncryptedMessage = "test message here"
            }
        };

        await callerContext.Initialize(ownerApiClient);

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);

        var callerDriveClient = new UniversalDriveApiClient(sender.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedContent64, _, _) =
            await callerDriveClient.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [], keyHeader);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            ClassicAssert.IsNotNull(updateFileResponse.Content);
            await callerDriveClient.WaitForEmptyOutbox(targetDrive);

            //
            // ensure the local file exists and is updated correctly
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.IsEncrypted);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedEncryptedContent64);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(header.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
            ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any());

            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);

            // ensure the recipients get the file

            foreach (var recipient in allRecipients)
            {
                var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);
                var recipientFileResponse =
                    await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
                var remoteFileHeader = recipientFileResponse.Content.SearchResults.FirstOrDefault();

                ClassicAssert.IsNotNull(remoteFileHeader);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.IsEncrypted);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.AppData.Content == updatedEncryptedContent64); //latest update
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
                ClassicAssert.IsFalse(remoteFileHeader.FileMetadata.Payloads.Any());

                // validate we can decrypt it on the recipient
                // remoteFileHeader.FileMetadata.AppData.Content

                var sharedSecret = recipientOwnerClient.GetTokenContext().SharedSecret;
                var remoteKeyHeader = remoteFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(keyHeader.Iv, remoteKeyHeader.Iv));
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(keyHeader.AesKey.GetKey(), remoteKeyHeader.AesKey.GetKey()));
                var decryptedBytes = remoteKeyHeader.Decrypt(remoteFileHeader.FileMetadata.AppData.Content.FromBase64());
                ClassicAssert.IsTrue(decryptedBytes.ToStringFromUtf8Bytes() == updatedContentToBeDistributed);


                // valid recipient got the notification
                var allNotificationsResponse = await recipientOwnerClient.AppNotifications.GetList(100);
                ClassicAssert.IsTrue(allNotificationsResponse.IsSuccessStatusCode);
                var notifications = allNotificationsResponse.Content;
                ClassicAssert.IsNotNull(notifications);

                ClassicAssert.IsNotNull(notifications.Results.SingleOrDefault(
                    n => n.SenderId == ownerApiClient.OdinId
                         && n.Options.TypeId == updateInstructionSet.AppNotificationOptions.TypeId
                         && n.Options.PeerSubscriptionId == updateInstructionSet.AppNotificationOptions.PeerSubscriptionId
                         && n.Options.UnEncryptedMessage == updateInstructionSet.AppNotificationOptions.UnEncryptedMessage
                         && n.Options.TagId == updateInstructionSet.AppNotificationOptions.TagId));
            }
        }

        await Disconnect(sender, allRecipients, targetDrive);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestHasWriteAccess))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1ThumbnailsWhenTargetFileDoesNotExistOnRemoteServer_Encrypted(
        IApiClientContext callerContext,
        HttpStatusCode expectedStatusCode)
    {
        var sender = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(sender);
        var targetDrive = callerContext.TargetDrive;
        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true,
            attributes: new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } });

        //
        // Setup - upload a new file with payloads 
        // 

        List<TestIdentity> recipients = [TestIdentities.Frodo, TestIdentities.Merry];

        await SetupRecipients(sender, recipients, targetDrive);

        var keyHeader = KeyHeader.NewRandom16();

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        const string originalUploadedContent = "some content here..";
        uploadedFileMetadata.AppData.Content = originalUploadedContent;

        var payloadToBeDeleted = SamplePayloadDefinitions.GetPayloadDefinitionWithThumbnail1();
        payloadToBeDeleted.Iv = ByteArrayUtil.GetRndByteArray(16);
        List<TestPayloadDefinition> testPayloads = [payloadToBeDeleted];

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var (uploadNewFileResponse, encryptedJsonContent64, _, _) = await ownerApiClient.DriveRedux.UploadNewEncryptedFile(
            targetDrive,
            keyHeader,
            uploadedFileMetadata,
            uploadManifest,
            testPayloads);

        ClassicAssert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;


        // verify setup

        var getHeaderToVerifyResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
        ClassicAssert.IsTrue(getHeaderToVerifyResponse.IsSuccessStatusCode);
        var headerToVerify = getHeaderToVerifyResponse.Content;
        ClassicAssert.IsNotNull(headerToVerify);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.IsEncrypted);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.AppData.Content == encryptedJsonContent64);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.AppData.DataType == uploadedFileMetadata.AppData.DataType);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.VersionTag == uploadResult.NewVersionTag);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.Payloads.Count == testPayloads.Count);
        ClassicAssert.IsTrue(headerToVerify.FileMetadata.Payloads.Any(pd => pd.Key == payloadToBeDeleted.Key),
            "payloadToBeDeleted should be in the initial file upload:)");


        var localSS = ownerApiClient.GetTokenContext().SharedSecret;
        var localKeyHeaderToVerify = headerToVerify.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref localSS);
        var localDecryptedBytes = localKeyHeaderToVerify.Decrypt(headerToVerify.FileMetadata.AppData.Content.FromBase64());
        ClassicAssert.IsTrue(localDecryptedBytes.ToStringFromUtf8Bytes() == originalUploadedContent);

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        const string updatedContentToBeDistributed = "some new content to be distributed";
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = updatedContentToBeDistributed;
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var r = recipients.Select(r => r.OdinId).ToList();
        var payloadToAdd = SamplePayloadDefinitions.GetPayloadDefinition1();
        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = r,
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
                        PayloadKey = payloadToBeDeleted.Key
                    }
                ]
            },
            UseAppNotification = true,
            AppNotificationOptions = new()
            {
                AppId = Guid.NewGuid(),
                TypeId = Guid.NewGuid(),
                TagId = Guid.NewGuid(),
                Silent = false,
                PeerSubscriptionId = Guid.NewGuid(),
                Recipients = r,
                UnEncryptedMessage = "test message here"
            }
        };

        await callerContext.Initialize(ownerApiClient);

        keyHeader.Iv = ByteArrayUtil.GetRndByteArray(16);

        var callerDriveClient = new UniversalDriveApiClient(sender.OdinId, callerContext.GetFactory());
        var (updateFileResponse, updatedEncryptedContent64, _, _) =
            await callerDriveClient.UpdateEncryptedFile(updateInstructionSet, updatedFileMetadata, [payloadToAdd], keyHeader);
        ClassicAssert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            ClassicAssert.IsNotNull(updateFileResponse.Content);
            await callerDriveClient.WaitForEmptyOutbox(targetDrive);

            //
            // ensure the local file exists and is updated correctly
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            ClassicAssert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            ClassicAssert.IsNotNull(header);
            ClassicAssert.IsTrue(header.FileMetadata.IsEncrypted);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.Content == updatedEncryptedContent64);
            ClassicAssert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            ClassicAssert.IsTrue(header.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
            ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
                "payloadToAdd should have been, well, added :)");
            ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToBeDeleted.Key),
                "payload 1 should have been removed:)");

            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            ClassicAssert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            ClassicAssert.IsNotNull(theFileSearchResult);
            ClassicAssert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);

            // ensure the recipients get the file

            foreach (var recipient in recipients)
            {
                var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);
                var recipientFileResponse =
                    await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
                var remoteFileHeader = recipientFileResponse.Content.SearchResults.FirstOrDefault();

                ClassicAssert.IsNotNull(remoteFileHeader);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.IsEncrypted);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.AppData.Content == updatedEncryptedContent64); //latest update
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
                ClassicAssert.IsTrue(remoteFileHeader.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
                ClassicAssert.IsTrue(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToAdd.Key),
                    "payloadToAdd should have been, well, added :)");
                ClassicAssert.IsFalse(header.FileMetadata.Payloads.Any(pd => pd.Key == payloadToBeDeleted.Key),
                    "payload 1 should have been removed:)");

                // validate we can decrypt it on the recipient
                // remoteFileHeader.FileMetadata.AppData.Content

                var sharedSecret = recipientOwnerClient.GetTokenContext().SharedSecret;
                var remoteKeyHeader = remoteFileHeader.SharedSecretEncryptedKeyHeader.DecryptAesToKeyHeader(ref sharedSecret);
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(keyHeader.Iv, remoteKeyHeader.Iv));
                ClassicAssert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(keyHeader.AesKey.GetKey(), remoteKeyHeader.AesKey.GetKey()));
                var decryptedBytes = remoteKeyHeader.Decrypt(remoteFileHeader.FileMetadata.AppData.Content.FromBase64());
                ClassicAssert.IsTrue(decryptedBytes.ToStringFromUtf8Bytes() == updatedContentToBeDistributed);


                // valid recipient got the notification
                var allNotificationsResponse = await recipientOwnerClient.AppNotifications.GetList(100);
                ClassicAssert.IsTrue(allNotificationsResponse.IsSuccessStatusCode);
                var notifications = allNotificationsResponse.Content;
                ClassicAssert.IsNotNull(notifications);

                ClassicAssert.IsNotNull(notifications.Results.SingleOrDefault(
                    n => n.SenderId == ownerApiClient.OdinId
                         && n.Options.TypeId == updateInstructionSet.AppNotificationOptions.TypeId
                         && n.Options.PeerSubscriptionId == updateInstructionSet.AppNotificationOptions.PeerSubscriptionId
                         && n.Options.UnEncryptedMessage == updateInstructionSet.AppNotificationOptions.UnEncryptedMessage
                         && n.Options.TagId == updateInstructionSet.AppNotificationOptions.TagId));
            }
        }

        await Disconnect(sender, recipients, targetDrive);
    }

    private async Task SetupRecipients(TestIdentity sender, List<TestIdentity> recipients, TargetDrive targetDrive)
    {
        // add target drive

        var senderClient = _scaffold.CreateOwnerApiClientRedux(sender);
        await senderClient.Configuration.DisableAutoAcceptIntroductions(true);

        foreach (var recipient in recipients)
        {
            var client = _scaffold.CreateOwnerApiClientRedux(recipient);
            await client.Configuration.DisableAutoAcceptIntroductions(true);

            ClassicAssert.IsTrue((await client.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true,
                    attributes: new() { { BuiltInDriveAttributes.IsCollaborativeChannel, bool.TrueString } }
                ))
                .IsSuccessStatusCode);

            var circleId = Guid.NewGuid();

            // grant circleId
            var grant = new PermissionSetGrantRequest
            {
                Drives =
                [
                    new DriveGrantRequest
                    {
                        PermissionedDrive = new()
                        {
                            Drive = targetDrive,
                            Permission = DrivePermission.Write
                        }
                    }
                ],
                PermissionSet = null
            };

            ClassicAssert.IsTrue((await client.Network.CreateCircle(circleId, "some circle", grant)).IsSuccessStatusCode);
            ClassicAssert.IsTrue((await client.Connections.SendConnectionRequest(sender.OdinId, [circleId])).IsSuccessStatusCode);
            ClassicAssert.IsTrue((await senderClient.Connections.AcceptConnectionRequest(recipient.OdinId, [])).IsSuccessStatusCode);
        }
    }

    private async Task Disconnect(TestIdentity sender, List<TestIdentity> recipients, TargetDrive targetDrive)
    {
        var senderClient = _scaffold.CreateOwnerApiClientRedux(sender);
        await senderClient.Configuration.DisableAutoAcceptIntroductions(true);
        foreach (var recipient in recipients)
        {
            var client = _scaffold.CreateOwnerApiClientRedux(recipient);
            await client.Connections.DisconnectFrom(sender.OdinId);
            await senderClient.Connections.DisconnectFrom(recipient.OdinId);
        }
    }
}