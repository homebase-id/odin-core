using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
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
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Peer.UpdateBatch;

public class UpdateBatchWithRecipientsRemoteUpsertTests
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
            new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive(), new TestPermissionKeyList(PermissionKeys.UseTransitWrite)),
            HttpStatusCode.OK
        };
    }

    public static IEnumerable GuestAllowed()
    {
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    public static IEnumerable WhenGuestOnlyHasReadAccess()
    {
        yield return new object[] { new GuestReadOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServer(IApiClientContext callerContext,
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

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;

        // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server
        var transitOptions = new TransitOptions { };

        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = recipients.Select(r => r.OdinId).ToList(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new UniversalDriveApiClient(sender.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            Assert.IsNotNull(updateFileResponse.Content);
            await callerDriveClient.WaitForEmptyOutbox(targetDrive);

            //
            // ensure the local file exists and is updated correctly
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            Assert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(header.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
            Assert.IsFalse(header.FileMetadata.Payloads.Any());

            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            Assert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileSearchResult);
            Assert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);

            // ensure the recipients get the file

            foreach (var recipient in recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(recipient);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
                var remoteFileHeader = recipientFileResponse.Content.SearchResults.FirstOrDefault();

                Assert.IsNotNull(remoteFileHeader);
                Assert.IsTrue(remoteFileHeader.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
                Assert.IsTrue(remoteFileHeader.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
                Assert.IsTrue(remoteFileHeader.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
                Assert.IsFalse(remoteFileHeader.FileMetadata.Payloads.Any());
            }
        }

        await Disconnect(sender, recipients, targetDrive);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWhenTargetFileDoesNotExistOnRemoteServerMixed(IApiClientContext callerContext,
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

        List<TestIdentity> recipientsWithOutTargetFile = [TestIdentities.Frodo, TestIdentities.Merry];
        List<TestIdentity> recipientsWithTargetFile = [TestIdentities.Samwise, TestIdentities.TomBombadil];

        var allRecipients = recipientsWithTargetFile.Concat(recipientsWithOutTargetFile).ToList();
        await SetupRecipients(sender, allRecipients, targetDrive);

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;

        // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server
        var transitOptions = new TransitOptions
        {
            Recipients = recipientsWithTargetFile.Select(r => r.OdinId.DomainName).ToList(),
        };

        var uploadNewFileResponse = await ownerApiClient.DriveRedux.UploadNewMetadata(targetDrive, uploadedFileMetadata, transitOptions);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = allRecipients.Select(r => r.OdinId).ToList(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors = []
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new UniversalDriveApiClient(sender.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            Assert.IsNotNull(updateFileResponse.Content);
            await callerDriveClient.WaitForEmptyOutbox(targetDrive);

            //
            // ensure the local file exists and is updated correctly
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            Assert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(header.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
            Assert.IsFalse(header.FileMetadata.Payloads.Any());

            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            Assert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileSearchResult);
            Assert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);

            // ensure the recipients get the file

            foreach (var recipient in allRecipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(recipient);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
                var remoteFileHeader = recipientFileResponse.Content.SearchResults.FirstOrDefault();

                Assert.IsNotNull(remoteFileHeader);
                Assert.IsTrue(remoteFileHeader.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
                Assert.IsTrue(remoteFileHeader.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
                Assert.IsTrue(remoteFileHeader.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
                Assert.IsFalse(remoteFileHeader.FileMetadata.Payloads.Any());
            }
        }

        await Disconnect(sender, allRecipients, targetDrive);
    }

    [Test]
    [TestCaseSource(nameof(OwnerAllowed))]
    [TestCaseSource(nameof(AppAllowed))]
    [TestCaseSource(nameof(GuestAllowed))]
    [TestCaseSource(nameof(WhenGuestOnlyHasReadAccess))]
    public async Task CanUpdateBatchAndDistributeToRecipientsWith1PayloadsAnd1ThumbnailsWhenTargetFileDoesNotExistOnRemoteServer(
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

        var uploadedFileMetadata = SampleMetadataData.Create(fileType: 100, acl: AccessControlList.Connected);
        uploadedFileMetadata.AllowDistribution = true;
        var transitOptions = new TransitOptions { }; // Note: no transit options on initial upload to ensure
        // the file does not exist on the remote server

        var uploadedPayloadDefinition = SamplePayloadDefinitions.GetPayloadDefinition1();
        var testPayloads = new List<TestPayloadDefinition>()
        {
            uploadedPayloadDefinition
        };

        var uploadManifest = new UploadManifest()
        {
            PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
        };

        var uploadNewFileResponse =
            await ownerApiClient.DriveRedux.UploadNewFile(targetDrive, uploadedFileMetadata, uploadManifest, testPayloads, transitOptions);
        Assert.IsTrue(uploadNewFileResponse.IsSuccessStatusCode);
        await ownerApiClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

        var uploadResult = uploadNewFileResponse.Content;
        var targetFile = uploadResult.File;
        var targetGlobalTransitIdFileIdentifier = uploadResult.GlobalTransitIdFileIdentifier;

        //
        // Act - call update batch with UpdateLocale = Local
        //

        // change around some data
        var updatedFileMetadata = uploadedFileMetadata;
        updatedFileMetadata.AppData.Content = "some new content here...";
        updatedFileMetadata.AppData.DataType = 991;
        updatedFileMetadata.VersionTag = uploadResult.NewVersionTag;

        var updateInstructionSet = new FileUpdateInstructionSet
        {
            Locale = UpdateLocale.Local,
            TransferIv = ByteArrayUtil.GetRndByteArray(16),
            File = targetFile.ToFileIdentifier(),
            Recipients = recipients.Select(r => r.OdinId).ToList(),
            Manifest = new UploadManifest
            {
                PayloadDescriptors =
                [
                    new UploadManifestPayloadDescriptor
                    {
                        PayloadUpdateOperationType = PayloadUpdateOperationType.DeletePayload,
                        PayloadKey = testPayloads.Single().Key,
                    }
                ]
            }
        };

        await callerContext.Initialize(ownerApiClient);

        var callerDriveClient = new UniversalDriveApiClient(sender.OdinId, callerContext.GetFactory());
        var updateFileResponse = await callerDriveClient.UpdateFile(updateInstructionSet, updatedFileMetadata, []);
        Assert.IsTrue(updateFileResponse.StatusCode == expectedStatusCode,
            $"Expected {expectedStatusCode} but actual was {updateFileResponse.StatusCode}");

        // Let's test more
        if (expectedStatusCode == HttpStatusCode.OK)
        {
            Assert.IsNotNull(updateFileResponse.Content);
            await callerDriveClient.WaitForEmptyOutbox(targetDrive);

            //
            // ensure the local file exists and is updated correctly
            //
            var getHeaderResponse = await ownerApiClient.DriveRedux.GetFileHeader(targetFile);
            Assert.IsTrue(getHeaderResponse.IsSuccessStatusCode);
            var header = getHeaderResponse.Content;
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
            Assert.IsTrue(header.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
            Assert.IsTrue(header.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
            Assert.IsFalse(header.FileMetadata.Payloads.Any());

            // Ensure we find the file on the recipient
            // 
            var searchResponse = await ownerApiClient.DriveRedux.QueryBatch(new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetFile.TargetDrive,
                    DataType = [updatedFileMetadata.AppData.DataType]
                },
                ResultOptionsRequest = QueryBatchResultOptionsRequest.Default
            });

            Assert.IsTrue(searchResponse.IsSuccessStatusCode);
            var theFileSearchResult = searchResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileSearchResult);
            Assert.IsTrue(theFileSearchResult.FileId == targetFile.FileId);

            // ensure the recipients get the file

            foreach (var recipient in recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(recipient);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(targetGlobalTransitIdFileIdentifier);
                var remoteFileHeader = recipientFileResponse.Content.SearchResults.FirstOrDefault();

                Assert.IsNotNull(remoteFileHeader);
                Assert.IsTrue(remoteFileHeader.FileMetadata.AppData.Content == updatedFileMetadata.AppData.Content);
                Assert.IsTrue(remoteFileHeader.FileMetadata.AppData.DataType == updatedFileMetadata.AppData.DataType);
                Assert.IsTrue(remoteFileHeader.FileMetadata.VersionTag == updateFileResponse.Content.NewVersionTag);
                Assert.IsFalse(remoteFileHeader.FileMetadata.Payloads.Any());

                var getPayloadResponse = await client.DriveRedux.GetPayload(new ExternalFileIdentifier()
                {
                    FileId = remoteFileHeader.FileId,
                    TargetDrive = targetGlobalTransitIdFileIdentifier.TargetDrive
                }, testPayloads.Single().Key);

                Assert.IsTrue(getPayloadResponse.StatusCode == HttpStatusCode.NotFound);
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

            Assert.IsTrue((await client.DriveManager.CreateDrive(targetDrive, "Test Drive 001", "", allowAnonymousReads: true,
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

            Assert.IsTrue((await client.Network.CreateCircle(circleId, "some circle", grant)).IsSuccessStatusCode);
            Assert.IsTrue((await client.Connections.SendConnectionRequest(sender.OdinId, [circleId])).IsSuccessStatusCode);
            Assert.IsTrue((await senderClient.Connections.AcceptConnectionRequest(recipient.OdinId, [])).IsSuccessStatusCode);
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