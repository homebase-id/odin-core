using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.DataSubscription;
using Odin.Services.Drives;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;

namespace Odin.Hosting.Tests._Universal.Peer.DirectSend
{
    public class PeerDirectSendTests3
    {
        private WebScaffold _scaffold;
        private readonly TimeSpan _debugTimeout = TimeSpan.FromHours(2);

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

        [Test]
        public async Task FailToUseStorageIntent_MetadataOnly_UnEncrypted_WithInvalidVersionTag()
        {
            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.Write;

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            const string fileContent1 = "tabc123";
            var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 2043, fileContent1, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;

            // Upload a file with 1 payload
            const string payloadContent = "this is for the biiiirrddss";
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = payloadContent.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var transferFileResponse = await senderOwnerClient.PeerDirect.TransferNewFile(recipientTargetDrive,
                fileMetadata,
                [recipient.OdinId],
                uploadManifest,
                testPayloads);

            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromHours(1));

            // validate recipient got the file and the payload are there
            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var header1 = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header1);

            var getPayloadResponse1 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = header1.FileMetadata.Payloads.FirstOrDefault()!.Key,
                File = new ExternalFileIdentifier()
                {
                    FileId = header1.FileId,
                    TargetDrive = recipientTargetDrive
                }
            });

            Assert.IsTrue(getPayloadResponse1.IsSuccessStatusCode);
            Assert.IsTrue((await getPayloadResponse1.Content.ReadAsStringAsync()) == payloadContent);


            //
            // Now: update this file using StorageIntent.Metadata only
            //

            const string fileContent2 = "updated content";
            fileMetadata.AppData.Content = fileContent2;
            var transferFileResponse2 = await senderOwnerClient.PeerDirect.UpdateRemoteFile(
                recipientTargetDrive,
                fileMetadata,
                [recipient.OdinId],
                transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                StorageIntent.MetadataOnly);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the header can be retrieved with new content and the payloads are same as before
            //

            Assert.IsTrue(transferFileResponse2.IsSuccessStatusCode);

            var getRemoteFileHeaderResponse2 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse2.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse2.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == fileContent2);
            Assert.IsTrue(header.FileMetadata.Payloads.Count == 1);

            var getPayloadResponse2 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = header1.FileMetadata.Payloads.FirstOrDefault()!.Key,
                File = new ExternalFileIdentifier()
                {
                    FileId = header1.FileId,
                    TargetDrive = recipientTargetDrive
                }
            });

            Assert.IsTrue(getPayloadResponse2.IsSuccessStatusCode);
            Assert.IsTrue((await getPayloadResponse2.Content.ReadAsStringAsync()) == payloadContent);

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailToUseStorageIntent_MetadataOnly_Encrypted_WithInvalidVersionTag()
        {
            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.Write;

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            const string fileContent1 = "tabc123";
            var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 2043, fileContent1, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;

            // Upload a file with 1 payload
            const string payloadContent = "this is for the biiiirrddss";
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = payloadContent.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var (transferFileResponse, _, _, uploadedPayloads) =
                await senderOwnerClient.PeerDirect.TransferNewEncryptedFile(recipientTargetDrive,
                    fileMetadata,
                    [recipient.OdinId],
                    uploadManifest,
                    testPayloads);

            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            var encryptedPayloadContent = uploadedPayloads.FirstOrDefault()!.EncryptedContent64;
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();
            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            // validate recipient got the file and the payload are there

            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var header1 = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header1);

            var getPayloadResponse1 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = header1.FileMetadata.Payloads.FirstOrDefault()!.Key,
                File = new ExternalFileIdentifier()
                {
                    FileId = header1.FileId,
                    TargetDrive = recipientTargetDrive
                }
            });

            Assert.IsTrue(getPayloadResponse1.IsSuccessStatusCode);
            Assert.IsTrue((await getPayloadResponse1.Content.ReadAsByteArrayAsync()).ToBase64() == encryptedPayloadContent);

            //
            // Now: update this file using StorageIntent.Metadata only
            //

            const string fileContent2 = "updated content";
            fileMetadata.AppData.Content = fileContent2;
            var (transferFileResponse2, encryptedJsonContent642, _, _) =
                await senderOwnerClient.PeerDirect.UpdateEncryptedRemoteFile(recipientTargetDrive, fileMetadata,
                    [recipient.OdinId],
                    transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                    StorageIntent.MetadataOnly);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the header can be retrieved with new content and the payloads are same as before
            //

            Assert.IsTrue(transferFileResponse2.IsSuccessStatusCode);

            var getRemoteFileHeaderResponse2 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse2.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse2.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == encryptedJsonContent642);
            Assert.IsTrue(header.FileMetadata.Payloads.Count == 1);

            var getPayloadResponse2 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = header1.FileMetadata.Payloads.FirstOrDefault()!.Key,
                File = new ExternalFileIdentifier()
                {
                    FileId = header1.FileId,
                    TargetDrive = recipientTargetDrive
                }
            });

            Assert.IsTrue(getPayloadResponse2.IsSuccessStatusCode);
            Assert.IsTrue((await getPayloadResponse2.Content.ReadAsByteArrayAsync()).ToBase64() == encryptedPayloadContent);

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailToAddMultipleRemote_EncryptedPayloads_ByKeyWhenMultiplePayloadsExist_WithInvalidVersionTag()
        {
            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.Write;

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            const string fileContent1 = "filecontent1";
            var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 2099, fileContent1, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;

            // Upload a file with 1 payload
            const string payloadContent = "some payload content";
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = payloadContent.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var (transferFileResponse, _, _, uploadedPayloads) =
                await senderOwnerClient.PeerDirect.TransferNewEncryptedFile(recipientTargetDrive,
                    fileMetadata,
                    [recipient.OdinId],
                    uploadManifest,
                    testPayloads);

            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            var encryptedPayloadContent = uploadedPayloads.FirstOrDefault()!.EncryptedContent64;
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            // Validate we have one payload 
            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);
            var remoteHeader = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(remoteHeader);
            Assert.IsTrue(remoteHeader.FileMetadata.Payloads.Count == testPayloads.Count);

            // validate recipient got the file and the payload are there

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Act: Add a new encrypted payload using an invalid version tag
            //

            //set an invalid version tag
            var targetVersionTag = Guid.NewGuid();
            // var targetVersionTag = remoteHeader.FileMetadata.VersionTag;

            var targetGlobalTransitId = transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId;
            const string p2Content = "aa233;d";
            const string payload2Key = "p2key111";

            const string p3Content = "3adfas";
            const string payload3Key = "p3key111";

            var newPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = payload2Key,
                    ContentType = "text/plain",
                    Content = p2Content.ToUtf8ByteArray(),
                    Thumbnails = []
                },
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = payload3Key,
                    ContentType = "text/plain",
                    Content = p3Content.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var newUploadsManifest = new UploadManifest()
            {
                PayloadDescriptors = newPayloads.ToPayloadDescriptorList().ToList()
            };

            var aesKey = ByteArrayUtil.GetRndByteArray(16);

            var (uploadPayloadsResponse, _) = await senderOwnerClient.PeerDirect.UploadEncryptedPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId], aesKey);

            // here we need a way to have a way to report the invalid version tag

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.Payloads.Count == 1);

            var remoteFile = new ExternalFileIdentifier()
            {
                FileId = header.FileId,
                TargetDrive = recipientTargetDrive
            };

            var getPayload1Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayload1Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload1Response.Content.ReadAsByteArrayAsync()).ToBase64() == encryptedPayloadContent);

            var getPayload2Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response.StatusCode == HttpStatusCode.NotFound);

            var getPayload3Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload3Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload3Response.StatusCode == HttpStatusCode.NotFound);

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailToUpdateMultipleRemote_EncryptedPayloads_ByKeyWhenMultiplePayloadsExist_WithInvalidVersionTag()
        {
            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.Write;

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            const string fileContent1 = "filecontent1";
            var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 2099, fileContent1, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;

            // Upload a file with 1 payload
            const string payloadContent = "some payload content";
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = payloadContent.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var (transferFileResponse, _, _, uploadedPayloads) =
                await senderOwnerClient.PeerDirect.TransferNewEncryptedFile(recipientTargetDrive,
                    fileMetadata,
                    [recipient.OdinId],
                    uploadManifest,
                    testPayloads);

            var originalPayloadCount = testPayloads.Count;

            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            var encryptedPayloadContent = uploadedPayloads.FirstOrDefault()!.EncryptedContent64;
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            // Validate we have one payload 
            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);
            var remoteHeader = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(remoteHeader);
            Assert.IsTrue(remoteHeader.FileMetadata.Payloads.Count == testPayloads.Count);

            var remoteFile = new ExternalFileIdentifier()
            {
                FileId = remoteHeader.FileId,
                TargetDrive = recipientTargetDrive
            };

            var getPayloadResponse = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue((await getPayloadResponse.Content.ReadAsByteArrayAsync()).ToBase64() == encryptedPayloadContent);

            //
            // Act: Add a new encrypted payload using an invalid version tag
            //

            //set an invalid version tag
            var targetVersionTag = Guid.NewGuid();
            // var targetVersionTag = remoteHeader.FileMetadata.VersionTag;

            var targetGlobalTransitId = transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId;

            const string updatedContent = "aa233;d";
            const string p2Content = "3adfas";
            const string payload2Key = "p2key111";

            var newPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = updatedContent.ToUtf8ByteArray(),
                    Thumbnails = []
                },
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = payload2Key,
                    ContentType = "text/plain",
                    Content = p2Content.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var newUploadsManifest = new UploadManifest()
            {
                PayloadDescriptors = newPayloads.ToPayloadDescriptorList().ToList()
            };

            var aesKey = ByteArrayUtil.GetRndByteArray(16);

            var (uploadPayloadsResponse, encryptedUpdatedPayloadContent64) = await senderOwnerClient.PeerDirect.UploadEncryptedPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId], aesKey);

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var updatedHeader = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(updatedHeader);
            Assert.IsTrue(updatedHeader.FileMetadata.Payloads.Count == originalPayloadCount, "there should be no additional payloads");

            var getPayload1Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayload1Response.IsSuccessStatusCode);
            var originalEncryptedPayload = uploadedPayloads.Single(k => k.Key == WebScaffold.PAYLOAD_KEY);
            Assert.IsTrue((await getPayload1Response.Content.ReadAsByteArrayAsync()).ToBase64() ==
                          originalEncryptedPayload.EncryptedContent64, "the payload content should not have changed");

            var getPayload2Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response.StatusCode == HttpStatusCode.NotFound);
            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailToAddMultipleRemote_Payloads_ByKeyWhenMultiplePayloadsExist_WithInvalidVersionTag()
        {
            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.Write;

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            const string fileContent1 = "filecontent1";
            var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 2099, fileContent1, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;

            // Upload a file with 1 payload
            const string payloadContent = "some payload content";
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = default,
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = payloadContent.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var transferFileResponse = await senderOwnerClient.PeerDirect.TransferNewFile(recipientTargetDrive,
                fileMetadata,
                [recipient.OdinId],
                uploadManifest,
                testPayloads);

            var originalPayloadCount = testPayloads.Count;

            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            // validate recipient got the file and the payload are there

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);


            // Validate we have one payload 
            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);
            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);
            var remoteHeader = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(remoteHeader);
            Assert.IsTrue(remoteHeader.FileMetadata.Payloads.Count == testPayloads.Count);


            //
            // Act: Add a new encrypted payload using an invalid version tag
            //

            //set an invalid version tag
            var targetVersionTag = Guid.NewGuid();
            // var targetVersionTag = remoteHeader.FileMetadata.VersionTag;


            var targetGlobalTransitId = transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId;
            const string p2Content = "aa233;d";
            const string payload2Key = "p2key111";

            const string p3Content = "3adfas";
            const string payload3Key = "p3key111";

            var newPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Key = payload2Key,
                    ContentType = "text/plain",
                    Content = p2Content.ToUtf8ByteArray(),
                    Thumbnails = []
                },
                new()
                {
                    Key = payload3Key,
                    ContentType = "text/plain",
                    Content = p3Content.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var newUploadsManifest = new UploadManifest()
            {
                PayloadDescriptors = newPayloads.ToPayloadDescriptorList().ToList()
            };

            var uploadPayloadsResponse = await senderOwnerClient.PeerDirect.UploadPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId]);

            //TODO: we need a way to detect the version tag mismatch error

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(
                recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.Payloads.Count == originalPayloadCount);

            var remoteFile = new ExternalFileIdentifier()
            {
                FileId = header.FileId,
                TargetDrive = recipientTargetDrive
            };

            var getPayload1Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayload1Response.IsSuccessStatusCode);
            Assert.IsTrue(await getPayload1Response.Content.ReadAsStringAsync() == payloadContent);

            var getPayload2Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });
            Assert.IsTrue(getPayload2Response.StatusCode == HttpStatusCode.NotFound);

            var getPayload3Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload3Key,
                File = remoteFile
            });
            Assert.IsTrue(getPayload3Response.StatusCode == HttpStatusCode.NotFound);

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailToUpdateMultipleRemote_Payloads_ByKeyWhenMultiplePayloadsExist_WithInvalidVersionTag()
        {
            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.Write;

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            const string fileContent1 = "filecontent1";
            var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 2099, fileContent1, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;

            // Upload a file with 1 payload
            const string payloadContent = "some payload content";
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = default,
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = payloadContent.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var transferFileResponse = await senderOwnerClient.PeerDirect.TransferNewFile(recipientTargetDrive,
                fileMetadata,
                [recipient.OdinId],
                uploadManifest,
                testPayloads);

            var originalPayloadCount = testPayloads.Count;
            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            // Validate we have one payload 
            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);
            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);
            var remoteHeader = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(remoteHeader);
            Assert.IsTrue(remoteHeader.FileMetadata.Payloads.Count == testPayloads.Count);

            var remoteFile = new ExternalFileIdentifier()
            {
                FileId = remoteHeader.FileId,
                TargetDrive = recipientTargetDrive
            };

            var getPayloadResponse = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue(await getPayloadResponse.Content.ReadAsStringAsync() == payloadContent);


            //
            // Act: Add a new encrypted payload using an invalid version tag
            //

            //set an invalid version tag
            var targetVersionTag = Guid.NewGuid();
            // var targetVersionTag = remoteHeader.FileMetadata.VersionTag;

            var targetGlobalTransitId = transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId;

            const string updatedContent = "aa233;d";
            const string p2Content = "some content for payload 2";
            const string payload2Key = "p2key111";

            var newPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = updatedContent.ToUtf8ByteArray(),
                    Thumbnails = []
                },
                new()
                {
                    Iv = ByteArrayUtil.GetRndByteArray(16),
                    Key = payload2Key,
                    ContentType = "text/plain",
                    Content = p2Content.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var newUploadsManifest = new UploadManifest()
            {
                PayloadDescriptors = newPayloads.ToPayloadDescriptorList().ToList()
            };

            var uploadPayloadsResponse = await senderOwnerClient.PeerDirect.UploadPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId]);

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var updatedHeader = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(updatedHeader);
            Assert.IsTrue(updatedHeader.FileMetadata.Payloads.Count == originalPayloadCount);

            var getPayload1Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayload1Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload1Response.Content.ReadAsStringAsync()) == payloadContent,
                "the original payload content should not have been updated");

            var getPayload2Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response.StatusCode == HttpStatusCode.NotFound);

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailToDeleteRemote_Payloads_ByKeyWhenMultiplePayloadsExist_WithInvalidVersionTag()
        {
            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.Write;

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            const string fileContent1 = "filecontent1";
            var fileMetadata = SampleMetadataData.CreateWithContent(fileType: 3011, fileContent1, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;

            // Upload a file with 2 payloads
            const string payload2Key = "test2_key";
            const string payloadContent1 = "some payload content";
            const string payloadContent2 = "another payloads content";
            var testPayloads = new List<TestPayloadDefinition>()
            {
                new()
                {
                    Iv = default,
                    Key = WebScaffold.PAYLOAD_KEY,
                    ContentType = "text/plain",
                    Content = payloadContent1.ToUtf8ByteArray(),
                    Thumbnails = []
                },
                new()
                {
                    Iv = default,
                    Key = payload2Key,
                    ContentType = "text/plain",
                    Content = payloadContent2.ToUtf8ByteArray(),
                    Thumbnails = []
                }
            };

            var uploadManifest = new UploadManifest()
            {
                PayloadDescriptors = testPayloads.ToPayloadDescriptorList().ToList()
            };

            var transferFileResponse = await senderOwnerClient.PeerDirect.TransferNewFile(recipientTargetDrive,
                fileMetadata,
                [recipient.OdinId],
                uploadManifest,
                testPayloads);

            var originalPayloadCount = testPayloads.Count;
            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            // Validate we have 2 payloads 
            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(
                recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);
            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);

            var remoteHeader = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(remoteHeader);
            Assert.IsTrue(remoteHeader.FileMetadata.Payloads.Count == originalPayloadCount);

            var remoteFile = new ExternalFileIdentifier()
            {
                FileId = remoteHeader.FileId,
                TargetDrive = recipientTargetDrive
            };

            var getPayload1Response1 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayload1Response1.IsSuccessStatusCode);
            Assert.IsTrue(await getPayload1Response1.Content.ReadAsStringAsync() == payloadContent1);

            var getPayload2Response1 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response1.IsSuccessStatusCode);
            Assert.IsTrue(await getPayload2Response1.Content.ReadAsStringAsync() == payloadContent2);

            //
            // Act: delete payload2Key; using invalid version tag
            //

            //set an invalid version tag
            var targetVersionTag = Guid.NewGuid();

            // var targetVersionTag = remoteHeader.FileMetadata.VersionTag;
            var targetGlobalTransitId = transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId;

            var deletePayloadResponse = await senderOwnerClient.PeerDirect.DeletePayload(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, payload2Key, [recipient.OdinId]);

            Assert.IsTrue(deletePayloadResponse.IsSuccessStatusCode);
            Assert.IsTrue(deletePayloadResponse.Content.RecipientStatus[recipient.OdinId] == OutboxEnqueuingStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive(_debugTimeout);
            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the payload was deleted but the header exists and has 1 payload
            //

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.QueryFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var updatedHeader = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(updatedHeader);
            Assert.IsTrue(updatedHeader.FileMetadata.Payloads.Count == originalPayloadCount);
            Assert.IsNotNull(updatedHeader.FileMetadata.Payloads.SingleOrDefault(p => p.Key == WebScaffold.PAYLOAD_KEY));

            var getPayload1Response2 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });
            Assert.IsTrue(getPayload1Response2.IsSuccessStatusCode);
            Assert.IsTrue(await getPayload1Response2.Content.ReadAsStringAsync() == payloadContent1);

            var getPayload2Response2 = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response2.IsSuccessStatusCode, "payload2 should still exist");
            Assert.IsTrue(await getPayload2Response2.Content.ReadAsStringAsync() == payloadContent2);

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        private async Task<TargetDrive> PrepareScenario(
            OwnerApiClientRedux senderOwnerClient,
            OwnerApiClientRedux recipientOwnerClient,
            DrivePermission drivePermissions)
        {
            var targetDrive = TargetDrive.NewTargetDrive();

            //
            // Recipient creates a target drive
            //
            Dictionary<string, string> isGroupChannelAttributes = new() { { FeedDriveDistributionRouter.IsCollaborativeChannel, bool.TrueString } };

            await recipientOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false,
                attributes: isGroupChannelAttributes);

            //
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = drivePermissions
            };

            var recipientCircleId = Guid.NewGuid();
            await recipientOwnerClient.Network.CreateCircle(recipientCircleId, "Circle with drive access", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            //
            // Sender sends connection request
            //
            await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, []);

            //
            // Recipient accepts; grants access to circle
            //
            await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, [recipientCircleId]);

            return targetDrive;
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}