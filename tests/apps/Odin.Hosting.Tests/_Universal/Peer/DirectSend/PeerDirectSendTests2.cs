using System;
using System.Collections.Generic;
using System.Linq;
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
    public class PeerDirectSendTests2
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
        public async Task CanUseStorageIntent_MetadataOnly_UnEncrypted()
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

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive, TimeSpan.FromHours(1));

            // validate recipient got the file and the payload are there

            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
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
            var transferFileResponse2 = await senderOwnerClient.PeerDirect.UpdateRemoteFile(recipientTargetDrive, fileMetadata,
                [recipient.OdinId],
                transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                StorageIntent.MetadataOnly);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the header can be retrieved with new content and the payloads are same as before
            //

            Assert.IsTrue(transferFileResponse2.IsSuccessStatusCode);

            var getRemoteFileHeaderResponse2 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
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
        public async Task CanUseStorageIntent_MetadataOnly_Encrypted()
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

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            var encryptedPayloadContent = uploadedPayloads.FirstOrDefault()!.EncryptedContent64;
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            // validate recipient got the file and the payload are there

            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
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
            var (transferFileResponse2, encryptedJsonContent64_2, _, _) =
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

            var getRemoteFileHeaderResponse2 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse2.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse2.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == encryptedJsonContent64_2);
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
        public async Task CanAddMultipleRemote_EncryptedPayloads_ByKeyWhenMultiplePayloadsExist()
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

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            var encryptedPayloadContent = uploadedPayloads.FirstOrDefault()!.EncryptedContent64;
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            // validate recipient got the file and the payload are there

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Act: Add a new encrypted payload
            //

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

            var targetVersionTag = Guid.Empty; //TODO?
            var (uploadPayloadsResponse, encryptedPayloads64) = await senderOwnerClient.PeerDirect.UploadEncryptedPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId], aesKey);

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);
            
            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.Payloads.Count == 3);

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

            Assert.IsTrue(getPayload2Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload2Response.Content.ReadAsByteArrayAsync()).ToBase64() == encryptedPayloads64[payload2Key].ToBase64());

            var getPayload3Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload3Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload3Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload3Response.Content.ReadAsByteArrayAsync()).ToBase64() == encryptedPayloads64[payload3Key].ToBase64());

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task CanUpdateMultipleRemote_EncryptedPayloads_ByKeyWhenMultiplePayloadsExist()
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

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            var encryptedPayloadContent = uploadedPayloads.FirstOrDefault()!.EncryptedContent64;
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);
            
            // Validate we have one payload 
            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);
            var remoteHeader = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(remoteHeader);
            Assert.IsTrue(remoteHeader.FileMetadata.Payloads.Count == 2);

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
            // Act: Add a new encrypted payload
            //

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

            var targetVersionTag = Guid.Empty; //TODO?
            var (uploadPayloadsResponse, encryptedUpdatedPayloadContent64) = await senderOwnerClient.PeerDirect.UploadEncryptedPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId], aesKey);

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var updatedHeader = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(updatedHeader);
            Assert.IsTrue(updatedHeader.FileMetadata.Payloads.Count == 2);

            var getPayload1Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayload1Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload1Response.Content.ReadAsByteArrayAsync()).ToBase64() ==
                          encryptedUpdatedPayloadContent64[WebScaffold.PAYLOAD_KEY].ToBase64());

            var getPayload2Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload2Response.Content.ReadAsByteArrayAsync()).ToBase64() == encryptedUpdatedPayloadContent64[payload2Key].ToBase64());

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }
        
        [Test]
        public async Task CanAddMultipleRemote_Payloads_ByKeyWhenMultiplePayloadsExist()
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

            var transferFileResponse = await senderOwnerClient.PeerDirect.TransferNewFile(recipientTargetDrive,
                fileMetadata,
                [recipient.OdinId],
                uploadManifest,
                testPayloads);

            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            // validate recipient got the file and the payload are there

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);


            //
            // Act: Add a new encrypted payload
            //
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

            var targetVersionTag = Guid.Empty; //TODO?
            var uploadPayloadsResponse = await senderOwnerClient.PeerDirect.UploadPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId]);

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.Payloads.Count == 3);

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
            Assert.IsTrue((await getPayload1Response.Content.ReadAsByteArrayAsync()).ToBase64() == payloadContent);

            var getPayload2Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload2Response.Content.ReadAsStringAsync()) == p2Content);

            var getPayload3Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload3Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload3Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload3Response.Content.ReadAsStringAsync()) == p3Content);

            await DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task CanUpdateMultipleRemote_Payloads_ByKeyWhenMultiplePayloadsExist()
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

            var transferFileResponse = await senderOwnerClient.PeerDirect.TransferNewFile(recipientTargetDrive,
                fileMetadata,
                [recipient.OdinId],
                uploadManifest,
                testPayloads);

            Assert.IsTrue(transferFileResponse.IsSuccessStatusCode);
            var transferResult = transferFileResponse.Content;

            Assert.IsTrue(transferResult.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            // Validate we have one payload 
            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);
            var remoteHeader = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(remoteHeader);
            Assert.IsTrue(remoteHeader.FileMetadata.Payloads.Count == 2);

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
            Assert.IsTrue((await getPayloadResponse.Content.ReadAsByteArrayAsync()).ToBase64() == payloadContent);


            //
            // Act: Add a new encrypted payload
            //

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

            var targetVersionTag = Guid.Empty; //TODO?
            var uploadPayloadsResponse = await senderOwnerClient.PeerDirect.UploadPayloads(targetGlobalTransitId,
                targetVersionTag, recipientTargetDrive, newUploadsManifest, newPayloads, [recipient.OdinId]);

            Assert.IsTrue(uploadPayloadsResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadPayloadsResponse.Content.RecipientStatus[recipient.OdinId] == TransferStatus.Enqueued);
            await senderOwnerClient.DriveRedux.WaitForEmptyOutboxForTransientTempDrive();

            await recipientOwnerClient.DriveRedux.ProcessInbox(recipientTargetDrive);

            //
            // Assert: the existing and new payloads can be retrieved and decrypted using the original key
            //

            //get the header
            var getRemoteFileHeaderResponse1 = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse1.IsSuccessStatusCode);
            var updatedHeader = getRemoteFileHeaderResponse1.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(updatedHeader);
            Assert.IsTrue(updatedHeader.FileMetadata.Payloads.Count == 2);

            var getPayload1Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = WebScaffold.PAYLOAD_KEY,
                File = remoteFile
            });

            Assert.IsTrue(getPayload1Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload1Response.Content.ReadAsStringAsync()) == updatedContent);

            var getPayload2Response = await senderOwnerClient.PeerQuery.GetPayload(new PeerGetPayloadRequest()
            {
                OdinId = recipient.OdinId,
                Key = payload2Key,
                File = remoteFile
            });

            Assert.IsTrue(getPayload2Response.IsSuccessStatusCode);
            Assert.IsTrue((await getPayload2Response.Content.ReadAsStringAsync()) == p2Content);

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