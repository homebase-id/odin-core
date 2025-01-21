using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests._Universal.Peer.ReadReceipt
{
    public class PeerReadReceiptTestsFailures
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);

            var env = new Dictionary<string, string>
            {
                { "Job__BackgroundJobStartDelaySeconds", "0" },
                { "Job__CronProcessingInterval", "1" },
                { "Job__EnableJobBackgroundService", "true" },
                { "Job__Enabled", "true" },
            };

            _scaffold.RunBeforeAnyTests(envOverrides: env);
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

        public static IEnumerable TestCases()
        {
            // yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
            // yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
            yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task FailToSendReadReceiptWhenRecipientDoesNotHaveWriteAccessToOriginalSendersDrive(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission senderDrivePermissions = DrivePermission.Write;
            const DrivePermission recipientDrivePermissions = DrivePermission.Read;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, senderDrivePermissions, recipientDrivePermissions);

            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles) =
                await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientOwnerClient, targetDrive, transitOptions);

            await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult.File.TargetDrive);

            await callerContext.Initialize(recipientOwnerClient);
            var driveClient = new UniversalDriveApiClient(recipientOwnerClient.Identity.OdinId, callerContext.GetFactory());

            //
            // Send the read receipt
            //
            var fileForReadReceipt = new ExternalFileIdentifier()
            {
                FileId = recipientFiles.Single().Value.FileId,
                TargetDrive = recipientFiles.Single().Value.TargetDrive
            };

            var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);
            await driveClient.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);

            Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
            var sendReadReceiptResult = sendReadReceiptResponse.Content;
            Assert.IsNotNull(sendReadReceiptResult);
            var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
            Assert.IsNotNull(item, "no record for file");
            var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
            Assert.IsNotNull(statusItem);
            Assert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);


            //TODO: there is no way to check the status of an item in the outbox; so the best
            //we can do is check if the target file is not updated

            //
            // Assert the read receipt was not updated on the sender's file
            //

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive);

            var getHistoryResponse = await senderOwnerClient.DriveRedux.GetTransferHistory(uploadResult.File);
            Assert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
            var theHistory = getHistoryResponse.Content;
            Assert.IsNotNull(theHistory);
            var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);
            
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsFalse(recipientStatus.IsReadByRecipient, "the file should not be marked as read");
            Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
            Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task FailToSendReadReceiptWhenNotConnectedOnRecipientSide(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var samOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission senderDrivePermissions = DrivePermission.Write;
            const DrivePermission recipientDrivePermissions = DrivePermission.Read;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(frodoOwnerClient, samOwnerClient, targetDrive, senderDrivePermissions, recipientDrivePermissions);

            var transitOptions = new TransitOptions()
            {
                Recipients = [samOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles) =
                await AssertCanUploadEncryptedMetadata(frodoOwnerClient, samOwnerClient, targetDrive, transitOptions);

            await samOwnerClient.DriveRedux.ProcessInbox(uploadResult.File.TargetDrive);

            await callerContext.Initialize(samOwnerClient);
            var driveClient = new UniversalDriveApiClient(samOwnerClient.Identity.OdinId, callerContext.GetFactory());

            //
            // Send the read receipt
            //
            var fileForReadReceipt = new ExternalFileIdentifier()
            {
                FileId = recipientFiles.Single().Value.FileId,
                TargetDrive = recipientFiles.Single().Value.TargetDrive
            };


            //
            // Severe the connection
            //
            await samOwnerClient.Connections.DisconnectFrom(frodoOwnerClient.Identity.OdinId);

            var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);

            Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
            var sendReadReceiptResult = sendReadReceiptResponse.Content;
            Assert.IsNotNull(sendReadReceiptResult);
            var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
            Assert.IsNotNull(item, "no record for file");
            var statusItem = item.Status.SingleOrDefault(i => i.Recipient == frodoOwnerClient.Identity.OdinId);
            Assert.IsNotNull(statusItem);
            Assert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.NotConnectedToOriginalSender);

            //
            // Assert the read receipt was not updated on the sender's file
            //

            await frodoOwnerClient.DriveRedux.ProcessInbox(targetDrive);

            var getHistoryResponse = await frodoOwnerClient.DriveRedux.GetTransferHistory(uploadResult.File);
            Assert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
            var theHistory = getHistoryResponse.Content;
            Assert.IsNotNull(theHistory);
            var recipientStatus = theHistory.GetHistoryItem(samOwnerClient.Identity.OdinId);
            
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsFalse(recipientStatus.IsReadByRecipient, "the file should not be marked as read");
            Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
            Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

            await frodoOwnerClient.Connections.DisconnectFrom(samOwnerClient.Identity.OdinId);
            await samOwnerClient.Connections.DisconnectFrom(frodoOwnerClient.Identity.OdinId);
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task FailToSendReadReceiptWhenNotConnectedOnSenderSide(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission senderDrivePermissions = DrivePermission.Write;
            const DrivePermission recipientDrivePermissions = DrivePermission.Read;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, senderDrivePermissions, recipientDrivePermissions);

            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles) =
                await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientOwnerClient, targetDrive, transitOptions);

            await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult.File.TargetDrive);

            await callerContext.Initialize(recipientOwnerClient);
            var driveClient = new UniversalDriveApiClient(recipientOwnerClient.Identity.OdinId, callerContext.GetFactory());

            //
            // Send the read receipt
            //
            var fileForReadReceipt = new ExternalFileIdentifier()
            {
                FileId = recipientFiles.Single().Value.FileId,
                TargetDrive = recipientFiles.Single().Value.TargetDrive
            };


            //
            // Severe the connection
            //
            await senderOwnerClient.Connections.DisconnectFrom(recipientOwnerClient.Identity.OdinId);

            var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);

            Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
            var sendReadReceiptResult = sendReadReceiptResponse.Content;
            Assert.IsNotNull(sendReadReceiptResult);
            var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
            Assert.IsNotNull(item, "no record for file");
            var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
            Assert.IsNotNull(statusItem);
            Assert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);


            //TODO: we cannot check if the original sender rejected the read-receipt because this
            //now in the outbox and there's no mechanism for that; therefore the best we can do is
            //validate the original sender file was not updated

            await driveClient.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);


            //
            // Assert the read receipt was not updated on the sender's file
            //

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive);

            var getHistoryResponse = await senderOwnerClient.DriveRedux.GetTransferHistory(uploadResult.File);
            Assert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
            var theHistory = getHistoryResponse.Content;
            Assert.IsNotNull(theHistory);
            var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);
            
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsFalse(recipientStatus.IsReadByRecipient, "the file should not be marked as read");
            Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
            Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

            await recipientOwnerClient.Connections.DisconnectFrom(senderOwnerClient.Identity.OdinId);
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        [Ignore("how do i test this scenario?")]
        public Task SendReadReceiptWhenOriginalSenderIdentityIsNotResponding(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            Assert.Inconclusive("");
            return Task.CompletedTask;
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        [Ignore("how do i test this scenario?")]
        public Task SendReadReceiptWithInvalidGlobalTransitId(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            Assert.Inconclusive("");
            return Task.CompletedTask;
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task FailToSendReadReceiptToSendersFiles(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission senderDrivePermissions = DrivePermission.Write;
            const DrivePermission recipientDrivePermissions = DrivePermission.Read;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, senderDrivePermissions, recipientDrivePermissions);

            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (senderUploadResult, _, _) =
                await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientOwnerClient, targetDrive, transitOptions);

            await recipientOwnerClient.DriveRedux.ProcessInbox(senderUploadResult.File.TargetDrive);

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            //
            // Send the read receipt
            //
            var fileForReadReceipt = new ExternalFileIdentifier()
            {
                FileId = senderUploadResult.File.FileId,
                TargetDrive = senderUploadResult.File.TargetDrive
            };

            var sendReadReceiptResponse = await driveClient.SendReadReceipt([fileForReadReceipt]);
            await driveClient.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);

            Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
            var sendReadReceiptResult = sendReadReceiptResponse.Content;
            Assert.IsNotNull(sendReadReceiptResult);
            var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
            Assert.IsNotNull(item);
            Assert.IsNull(item.Status.Single().Recipient);
            Assert.IsTrue(item.Status.Single().Status == SendReadReceiptResultStatus.CannotSendReadReceiptToSelf);

            //
            // Assert the read receipt was not updated on the sender's file
            //

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive);

            var getHistoryResponse = await senderOwnerClient.DriveRedux.GetTransferHistory(senderUploadResult.File);
            Assert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
            var theHistory = getHistoryResponse.Content;
            Assert.IsNotNull(theHistory);
            var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsFalse(recipientStatus.IsReadByRecipient, "the file should not be marked as read");
            Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
            Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == senderUploadResult.NewVersionTag);

            await senderOwnerClient.Connections.DisconnectFrom(recipientOwnerClient.Identity.OdinId);
            await recipientOwnerClient.Connections.DisconnectFrom(senderOwnerClient.Identity.OdinId);
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        [Ignore("how do i test this scenario?")]
        public Task SendReadReceiptWhenNeverHaveReceivedTheOriginalFile(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            Assert.Inconclusive("");
            return Task.CompletedTask;
        }

        private async Task<(UploadResult response, string encryptedJsonContent64, Dictionary<string, SharedSecretEncryptedFileHeader>
                recipientFiles)>
            AssertCanUploadEncryptedMetadata(
                OwnerApiClientRedux senderOwnerClient,
                OwnerApiClientRedux recipientOwnerClient,
                TargetDrive targetDrive,
                TransitOptions transitOptions)
        {
            const string uploadedContent = "pie";

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = true,
                AppData = new()
                {
                    Content = uploadedContent,
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

            var (uploadResponse, encryptedJsonContent64) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var senderUploadResult = uploadResponse.Content;
            Assert.IsNotNull(senderUploadResult);
            Assert.IsTrue(senderUploadResult.RecipientStatus.Count == 1);
            Assert.IsTrue(senderUploadResult.RecipientStatus[transitOptions.Recipients.Single()] == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(storageOptions.Drive);

            // validate recipient got the file

            await recipientOwnerClient.DriveRedux.ProcessInbox(senderUploadResult.File.TargetDrive);

            var recipientFiles = new Dictionary<string, SharedSecretEncryptedFileHeader>();
            foreach (var recipient in transitOptions.Recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(senderUploadResult.GlobalTransitIdFileIdentifier);
                Assert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
                var file = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                Assert.IsNotNull(file);
                recipientFiles.Add(recipient, file);
            }


            return (senderUploadResult, encryptedJsonContent64, recipientFiles);
        }

        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient, TargetDrive targetDrive,
            DrivePermission drivePermissionsGrantedToSender, DrivePermission drivePermissionsGrantedToRecipient)
        {
            //
            // Recipient creates a target drive
            //
            var recipientDriveResponse = await recipientOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            Assert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

            //
            // Sender needs this same drive in order to send across files
            //
            var senderDriveResponse = await senderOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on sender",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            Assert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

            var senderCircleId = Guid.NewGuid();
            var createCircleOnSenderResponse = await senderOwnerClient.Network.CreateCircle(senderCircleId,
                "Circle with drive access for the recipient to send back a read-receipt", new PermissionSetGrantRequest()
                {
                    Drives = new List<DriveGrantRequest>()
                    {
                        new()
                        {
                            PermissionedDrive = new PermissionedDrive()
                            {
                                Drive = targetDrive,
                                Permission = drivePermissionsGrantedToRecipient
                            }
                        }
                    }
                });

            Assert.IsTrue(createCircleOnSenderResponse.IsSuccessStatusCode);


            var expectedPermissionedDriveForSender = new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = drivePermissionsGrantedToSender
            };
            var recipientCircleId = Guid.NewGuid();
            var createCircleOnRecipientResponse = await recipientOwnerClient.Network.CreateCircle(recipientCircleId, "Circle with drive access",
                new PermissionSetGrantRequest()
                {
                    Drives = new List<DriveGrantRequest>()
                    {
                        new()
                        {
                            PermissionedDrive = expectedPermissionedDriveForSender
                        }
                    }
                });

            Assert.IsTrue(createCircleOnRecipientResponse.IsSuccessStatusCode);

            //
            // Sender sends connection request
            //
            await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, [senderCircleId]);

            //
            // Recipient accepts; grants access to circle
            //
            await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, [recipientCircleId]);

            // 
            // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var getConnectionInfoResponse = await recipientOwnerClient.Network.GetConnectionInfo(senderOwnerClient.Identity.OdinId);

            Assert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
            var senderConnectionInfo = getConnectionInfoResponse.Content;

            Assert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDriveForSender)));
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}