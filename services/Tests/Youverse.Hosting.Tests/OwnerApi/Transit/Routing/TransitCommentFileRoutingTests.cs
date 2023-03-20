using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.Transit.Routing
{
    public class TransitCommentFileRoutingTests
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


        [Test]
        public async Task CanTransfer_Unencrypted_Comment_S2110()
        {
            /*
             Success Test - Comment
                Valid ReferencedFile (global transit id)
                Sender has storage Key
                Sender has write access
                Upload standard file - encrypted = false
                Upload comment file - encrypted = false
                Should succeed (S2110)
                    Direct write comment
                    Comment is not distributed
                    ReferencedFile summary updated
                    ReferencedFile is distributed to followers
             */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.ReadWrite;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = false;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = false;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var standardFileUploadResult = await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.JsonContent == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.PayloadIsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.DeliveredToTargetDrive, $"Should have been delivered, actual status was {recipientStatus}");

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentUploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentUploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentUploadResult.GlobalTransitId);

            //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public void CanTransfer_Encrypted_Comment_S2110()
        {
            Assert.Inconclusive("work in progress");

            /*
             Success Test - Comment
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender has write access
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should succeed (S2110)
                    Direct write comment
                    Comment is not distributed
                    ReferencedFile summary updated
                    ReferencedFile is distributed to followers
             */
        }

        [Test]
        public void FailsWhenSenderCannotWriteCommentOnRecipientServer()
        {
            Assert.Inconclusive("work in progress");

            /*
             Failure Test - Comment
                Fails when sender cannot write to target drive on recipients server
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender does not have write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                throws 403 - S2010
             */
        }

        [Test]
        public void FailsWhenSenderSpecifiesInvalidReferencedFile_S2030()
        {
            Assert.Inconclusive("work in progress");

            /*
             Fails when sender provides invalid  global transit id
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender does not have write access (S2000)
                Sender has storage Key
                Invalid ReferencedFile (global transit id)
                Should fail
                throws Bad Request - S2030
             */
        }

        [Test]
        public void FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test1()
        {
            Assert.Inconclusive("work in progress");

            /*
             Fails when encryption do not match between from a comment to its ReferencedFile
                Test 1
                Upload standard file - encrypted = true
                Upload comment file - encrypted = false
                Sender does not have write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                Bad Request (S2100)
             */
        }

        [Test]
        public void FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test2()
        {
            Assert.Inconclusive("work in progress");

            /*
              Fails when encryption do not match between from a comment to its ReferencedFile

                Test 2
                Upload standard file - encrypted = false
                Upload comment file - encrypted = true
                Sender does not have write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                Bad Request (S2100)
             */
        }

        [Test]
        public void FailsWhenCommentFileIsEncryptedAndSenderHasNoDriveStorageKeyOnRecipientServer_S2210()
        {
            Assert.Inconclusive("work in progress");

            /*
             Fails when file is encrypted and there is no drive storage key
                Comment:
                Test 1
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender has write access
                Sender does not have storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                403 (question outstanding, why not go to inbox
             */
        }

        //

        /// <summary>
        /// Sends a standard file to a single recipient and performs basic assertions required by all tests
        /// </summary>
        private async Task<(UploadResult, string encryptedJsonContent64)> TransferComment(
            OwnerApiClient sender,
            GlobalTransitIdFileIdentifier referencedFile,
            string uploadedContent,
            bool encrypted,
            TestIdentity recipient)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                ContentType = "application/json",
                PayloadIsEncrypted = encrypted,

                //indicates the file about which this file is giving feed back
                ReferencedFile = referencedFile,

                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = uploadedContent,
                    FileType = default,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.Connected
            };

            var storageOptions = new StorageOptions()
            {
                Drive = referencedFile.TargetDrive
            };

            var transitOptions = new TransitOptions()
            {
                Recipients = new List<string>() { recipient.OdinId },
                IsTransient = true,
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendNowAwaitResponse,
                OverrideTargetDrive = default
            };

            UploadResult uploadResult;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (uploadResult, encryptedJsonContent64) = await sender.Transit.TransferEncryptedFile(
                    FileSystemType.Comment,
                    fileMetadata,
                    storageOptions,
                    transitOptions,
                    payloadData: string.Empty
                );
            }
            else
            {
                uploadResult = await sender.Transit.TransferFile(
                    FileSystemType.Comment,
                    fileMetadata,
                    storageOptions,
                    transitOptions,
                    payloadData: string.Empty
                );
            }

            //
            // Basic tests first which apply to all calls
            //
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);

            return (uploadResult, encryptedJsonContent64);
        }


        private async Task<TargetDrive> PrepareScenario(
            OwnerApiClient senderOwnerClient,
            OwnerApiClient recipientOwnerClient,
            DrivePermission drivePermissions)
        {
            //
            // Recipient creates a target drive
            //
            var recipientTargetDrive = await recipientOwnerClient.Drive.CreateDrive(
                targetDrive: TargetDrive.NewTargetDrive(),
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            //
            // Sender needs this same drive in order to send across files
            //
            var senderTargetDrive = await senderOwnerClient.Drive.CreateDrive(
                targetDrive: recipientTargetDrive.TargetDriveInfo,
                name: "Target drive on sender",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);


            //
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = recipientTargetDrive.TargetDriveInfo,
                Permission = drivePermissions
            };

            var recipientCircle = await recipientOwnerClient.Network.CreateCircle("Circle with drive access", new PermissionSetGrantRequest()
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
            await senderOwnerClient.Network.SendConnectionRequest(recipientOwnerClient.Identity, new List<GuidId>() { });

            //
            // Recipient accepts; grants access to circle
            //
            await recipientOwnerClient.Network.AcceptConnectionRequest(senderOwnerClient.Identity, new List<GuidId>() { recipientCircle.Id });

            // 
            // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var senderConnectionInfo = await recipientOwnerClient.Network.GetConnectionInfo(senderOwnerClient.Identity);

            Assert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == recipientCircle.DriveGrants.Single().PermissionedDrive)));

            return recipientTargetDrive.TargetDriveInfo;
        }

        private async Task<UploadResult> UploadStandardFile(OwnerApiClient client, TargetDrive targetDrive, string uploadedContent, bool encrypted)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                ContentType = "application/json",
                PayloadIsEncrypted = encrypted,
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = uploadedContent,
                    FileType = 200,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.Connected
            };

            if (encrypted)
            {
                return await client.Drive.UploadEncryptedFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
            }

            return await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
        }

        private async Task DeleteScenario(OwnerApiClient senderOwnerClient, OwnerApiClient recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}