using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    public class TransitStandardFileRoutingTests
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
        public async Task CanTransfer_Unencrypted_StandardFileAndDirectWrite_S1110()
        {
            /*
                Success Test - Standard File
                    Upload standard file - encrypted = false
                    Sender has write access
                    Sender has storage key and read access
                    Should succeed
                    Perform direct write (S1110)
                    File is distributed to followers -TODO: need to figure out distribution stuff
            */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.ReadWrite;
            const string uploadedContent = "We eagles fly to Mordor, sup w/ that?";
            const bool isEncrypted = false;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);
            var (uploadResult, _) = await this.SendStandardFile(senderOwnerClient, targetDrive, uploadedContent, encrypted: isEncrypted, recipient);

            Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
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
                TargetDrive = uploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == isEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == uploadedContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

            //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task CanTransfer_Encrypted_StandardFileAndMoveToInbox_S1210_and_S1220()
        {
            /*
                Success Test - Standard File
                    Upload standard file - encrypted = true
                    Sender has write access
                    sender does not have storage key
                    Should succeed
                    File goes to inbox (S1210, S1220)
             */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Write;
            const string uploadedContent = "Three of us eagles, coming to save Frodo, Sam, and Smegol";
            const bool isEncrypted = true;
            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);
            var (uploadResult, encryptedJsonContent64) = await this.SendStandardFile(senderOwnerClient,
                targetDrive, uploadedContent, encrypted: isEncrypted, recipient);

            Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.DeliveredToInbox, $"Should have been delivered, actual status was {recipientStatus}");

            //
            //  Assert recipient does not have the file when it is first sent
            //
            var qp = new FileQueryParams()
            {
                TargetDrive = uploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var emptyBatch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
            Assert.IsFalse(emptyBatch.SearchResults.Any());

            //
            await recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // Now the File should be on recipient server and accessible by global transit id
            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == isEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == encryptedJsonContent64);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == uploadResult.GlobalTransitId);

            //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailsWhenSenderCannotWriteToTargetDriveOnRecipientServer_S1010()
        {
            /*
             Failure Test - Standard
                Fails when sender cannot write to target drive on recipients server
                Upload standard file - encrypted = true
                Sender does not have write access
                sender has  storage key
                Should succeed
                Throws 403
             */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read;
            const string uploadedContent = "But we only got Frodo and Sam, thank you Smegol";
            const bool isEncrypted = false;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);
            var (uploadResult, _) = await this.SendStandardFile(senderOwnerClient, targetDrive, uploadedContent, encrypted: isEncrypted, recipient);

            Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.RecipientReturnedAccessDenied, $"Should have been delivered, actual status was {recipientStatus}");

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = uploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { uploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Standard, qp);
            Assert.IsFalse(batch.SearchResults.Any());
            
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        /// <summary>
        /// Sends a standard file to a single recipient and performs basic assertions required by all tests
        /// </summary>
        private async Task<(UploadResult, string encryptedJsonContent64)> SendStandardFile(OwnerApiClient sender, TargetDrive targetDrive,
            string uploadedContent, bool encrypted,
            TestIdentity recipient)
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
                    FileType = default,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.Connected
            };

            var storageOptions = new StorageOptions()
            {
                Drive = targetDrive
            };

            var transitOptions = new TransitOptions()
            {
                Recipients = new List<string>() { recipient.OdinId },
                IsTransient = false,
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendNowAwaitResponse,
                OverrideTargetDrive = default
            };

            UploadResult uploadResult;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (uploadResult, encryptedJsonContent64) = await sender.Transit.TransferEncryptedFile(
                    FileSystemType.Standard,
                    fileMetadata,
                    storageOptions,
                    transitOptions,
                    payloadData: string.Empty
                );
            }
            else
            {
                uploadResult = await sender.Transit.TransferFile(
                    FileSystemType.Standard,
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

        private async Task<UploadResult> UploadComment(OwnerApiClient client, TargetDrive targetDrive, GlobalTransitIdFileIdentifier referencedFile,
            string commentContent, bool allowDistribution)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = allowDistribution,
                ContentType = "application/json",
                PayloadIsEncrypted = false,

                //indicates the file about which this file is giving feed back
                ReferencedFile = referencedFile,

                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = commentContent,
                    FileType = default,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.OwnerOnly
            };

            return await client.Drive.UploadFile(FileSystemType.Comment, targetDrive, fileMetadata);
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

        private async Task DeleteScenario(OwnerApiClient senderOwnerClient, OwnerApiClient recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}