using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Drive;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

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
        public async Task CanUseStorageIntent_MetadataOnly()
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
            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            // Now: update this file using StorageIntent.Metadata only

            const string fileContent2 = "098978";
            fileMetadata.AppData.Content = fileContent2;
            var transferFileResponse2 = await senderOwnerClient.PeerDirect.UpdateFile(recipientTargetDrive, fileMetadata,
                [recipient.OdinId],
                transferResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                StorageIntent.MetadataOnly);

            // validation: the header can be retrieved with new content

            Assert.IsTrue(transferFileResponse2.IsSuccessStatusCode);

            var getRemoteFileHeaderResponse = await senderOwnerClient.PeerQuery.GetFileHeaderByGlobalTransitId(recipient.OdinId,
                transferResult.RemoteGlobalTransitIdFileIdentifier);

            Assert.IsTrue(getRemoteFileHeaderResponse.IsSuccessStatusCode);
            var header = getRemoteFileHeaderResponse.Content.SearchResults.FirstOrDefault();
            Assert.IsNotNull(header);
            Assert.IsTrue(header.FileMetadata.AppData.Content == fileContent2);
        }

        [Test]
        public async Task CanUpdateRemotePayloadByKey()
        {
            // An encrypted file is first sent via peer direct
            //  the file has a header and 1 payload

            // this payload is updated

            // validation: the payload can be retrieved and decrypted using the original key
        }

        [Test]
        public async Task CanAddRemotePayloadByKey()
        {
            // An encrypted file is first sent via peer direct
            //  the file has a header and 1 payload

            // this a new payload is added using the updatepayload endpoint

            // validation: the existing and new payloads can be retrieved and decrypted using the original key
        }

        [Test]
        public async Task CanUpdateMultipleRemotePayloadByKeyWhenMultiplePayloadsExist()
        {
            // An encrypted file is first sent via peer direct
            //  the file has a header and 3 payloads

            // the second and third payloads are updated using the updatepayload endpoint

            // validation: the existing and new payloads can be retrieved and decrypted using the original key
        }

        [Test]
        public async Task CanTransfer_Unencrypted_Comment()
        {
            var sender = TestIdentities.Frodo; //sender is the one who sends the comment
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = false;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = false;

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, _) =
                await UploadStandardFile(recipientOwnerClient, recipientTargetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGtidResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            var recipientFileByGlobalTransitId = recipientFileByGtidResponse.Content?.SearchResults.SingleOrDefault();
            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            // Sender replies with a comment
            var (commentTransitResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                    GlobalTransitId = new List<Guid>()
                    {
                        commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId
                    }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true,
                    IncludeTransferHistory = false
                }
            };

            var batchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var batch = batchResponse.Content;

            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }


        /// <summary>
        /// Sends a standard file to a single recipient and performs basic assertions required by all tests
        /// </summary>
        private async Task<(TransitResult, string encryptedJsonContent64)> TransferComment(
            OwnerApiClientRedux sender,
            GlobalTransitIdFileIdentifier referencedFile,
            string uploadedContent,
            bool encrypted,
            TestIdentity recipient,
            Guid? overwriteFile = null,
            Guid? versionTag = null)
        {
            var fileMetadata = SampleMetadataData.CreateWithContent(default, uploadedContent, AccessControlList.Connected);
            fileMetadata.VersionTag = versionTag;
            fileMetadata.AllowDistribution = true;
            fileMetadata.IsEncrypted = encrypted;
            fileMetadata.ReferencedFile = referencedFile; //indicates the file about which this file is giving feed back

            var recipients = new List<string> { recipient.OdinId };

            ApiResponse<TransitResult> transitResultResponse;

            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (transitResultResponse, encryptedJsonContent64) = await sender.PeerDirect.TransferEncryptedMetadata(
                    remoteTargetDrive: referencedFile.TargetDrive,
                    fileMetadata,
                    recipients: recipients,
                    overwriteGlobalTransitFileId: overwriteFile,
                    fileSystemType: FileSystemType.Comment
                );
            }

            else
            {
                transitResultResponse = await sender.PeerDirect.TransferMetadata(
                    referencedFile.TargetDrive,
                    fileMetadata,
                    recipients: recipients,
                    overwriteFile,
                    fileSystemType: FileSystemType.Comment
                );
            }

            Assert.IsTrue(transitResultResponse.IsSuccessStatusCode);
            var transitResult = transitResultResponse.Content;

            //
            // Basic tests first which apply to all calls
            //
            Assert.IsTrue(transitResult.RecipientStatus.Count == 1);


            await sender.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
            return (transitResult, encryptedJsonContent64);
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
            await recipientOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

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

        private async Task<(UploadResult, string encryptedJsonContent64)> UploadStandardFile(OwnerApiClientRedux client, TargetDrive targetDrive,
            string uploadedContent, bool encrypted)
        {
            var fileMetadata = SampleMetadataData.CreateWithContent(200, uploadedContent, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;
            fileMetadata.IsEncrypted = encrypted;

            ApiResponse<UploadResult> uploadResponse;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (uploadResponse, encryptedJsonContent64) =
                    await client.DriveRedux.UploadNewEncryptedMetadata(targetDrive, fileMetadata);
            }
            else
            {
                uploadResponse = await client.DriveRedux.UploadNewMetadata(targetDrive, fileMetadata);
            }

            return (uploadResponse.Content, encryptedJsonContent64);
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}