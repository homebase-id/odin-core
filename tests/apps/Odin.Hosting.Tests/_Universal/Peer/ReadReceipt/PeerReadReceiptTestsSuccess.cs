using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
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
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests._Universal.Peer.ReadReceipt
{
    public class PeerReadReceiptTestsSuccess
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

            _scaffold.RunBeforeAnyTests(envOverrides: env, testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
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
        public async Task CanSendReadReceipt(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

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

            ClassicAssert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
            var sendReadReceiptResult = sendReadReceiptResponse.Content;
            ClassicAssert.IsNotNull(sendReadReceiptResult);
            var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
            ClassicAssert.IsNotNull(item, "no record for file");
            var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
            ClassicAssert.IsNotNull(statusItem);
            ClassicAssert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

            //
            // Assert the read receipt was updated on the sender's file
            //

            await recipientOwnerClient.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive);

            var getHistoryResponse = await senderOwnerClient.DriveRedux.GetTransferHistory(uploadResult.File);
            ClassicAssert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
            var theHistory = getHistoryResponse.Content;
            ClassicAssert.IsNotNull(theHistory);
            var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);
            
            ClassicAssert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            ClassicAssert.IsTrue(recipientStatus.IsReadByRecipient);
            ClassicAssert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
            ClassicAssert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

            _scaffold.AssertHasDebugLogEvent(message: PeerInboxProcessor.ReadReceiptItemMarkedComplete, count: 1);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task CanSendMultipleReadReceipts(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            // send sam two files
            var (senderUploadResult1, _, recipientFiles1) = await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientOwnerClient, targetDrive,
                new TransitOptions()
                {
                    Recipients = [recipientOwnerClient.Identity.OdinId]
                });

            var (senderUploadResult2, _, recipientFiles2) = await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientOwnerClient, targetDrive,
                new TransitOptions()
                {
                    Recipients = [recipientOwnerClient.Identity.OdinId]
                });

            await callerContext.Initialize(recipientOwnerClient);
            var samDriveClient = new UniversalDriveApiClient(recipientOwnerClient.Identity.OdinId, callerContext.GetFactory());

            //
            // Sam Sends the read receipt
            //
            var fileForReadReceipt1 = new ExternalFileIdentifier()
            {
                FileId = recipientFiles1[recipientOwnerClient.Identity.OdinId].FileId,
                TargetDrive = recipientFiles1[recipientOwnerClient.Identity.OdinId].TargetDrive
            };

            var fileForReadReceipt2 = new ExternalFileIdentifier()
            {
                FileId = recipientFiles2[recipientOwnerClient.Identity.OdinId].FileId,
                TargetDrive = recipientFiles2[recipientOwnerClient.Identity.OdinId].TargetDrive
            };

            var samSendReadReceiptResponse = await samDriveClient.SendReadReceipt([fileForReadReceipt1, fileForReadReceipt2]);

            await samDriveClient.WaitForEmptyOutbox(fileForReadReceipt1.TargetDrive);
            await samDriveClient.WaitForEmptyOutbox(fileForReadReceipt2.TargetDrive);

            ClassicAssert.IsTrue(samSendReadReceiptResponse.IsSuccessStatusCode);
            var samSendReadReceiptResult = samSendReadReceiptResponse.Content;
            ClassicAssert.IsNotNull(samSendReadReceiptResult);

            //
            //Assert both files read-receipt was accepted into the inbox
            //
            var item1 = samSendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt1);
            ClassicAssert.IsNotNull(item1, "no record for file 1");
            var statusItem1 = item1.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
            ClassicAssert.IsNotNull(statusItem1);
            ClassicAssert.IsTrue(statusItem1.Status == SendReadReceiptResultStatus.Enqueued);

            var item2 = samSendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt2);
            ClassicAssert.IsNotNull(item2, "no record for file 2");
            var statusItem2 = item2.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
            ClassicAssert.IsNotNull(statusItem2);
            ClassicAssert.IsTrue(statusItem2.Status == SendReadReceiptResultStatus.Enqueued);

            await recipientOwnerClient.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt1.TargetDrive);
            await recipientOwnerClient.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt2.TargetDrive);
            
            //
            // Assert the read receipt was updated on the sender's file
            //

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100);

            var getHistoryResponse1 = await senderOwnerClient.DriveRedux.GetTransferHistory(senderUploadResult1.File);
            ClassicAssert.IsTrue(getHistoryResponse1.IsSuccessStatusCode);
            var file1TransferHistory = getHistoryResponse1.Content;
            ClassicAssert.IsNotNull(file1TransferHistory);
            var samRecipientStatus1 = file1TransferHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);

            ClassicAssert.IsTrue(file1TransferHistory.History.Results.Count == 1);
            ClassicAssert.IsNotNull(samRecipientStatus1, "There should be a status update for the sam");
            ClassicAssert.IsTrue(samRecipientStatus1.IsReadByRecipient);
            ClassicAssert.IsTrue(samRecipientStatus1.LatestTransferStatus == LatestTransferStatus.Delivered);
            ClassicAssert.IsTrue(samRecipientStatus1.LatestSuccessfullyDeliveredVersionTag == senderUploadResult1.NewVersionTag);
            
            
            var getHistoryResponse2 = await senderOwnerClient.DriveRedux.GetTransferHistory(senderUploadResult2.File);
            ClassicAssert.IsTrue(getHistoryResponse2.IsSuccessStatusCode);
            var file1TransferHistory2 = getHistoryResponse2.Content;
            ClassicAssert.IsNotNull(file1TransferHistory2);
            var samRecipientStatus2 = file1TransferHistory2.GetHistoryItem(recipientOwnerClient.Identity.OdinId);

            ClassicAssert.IsTrue(file1TransferHistory2.History.Results.Count == 1);
            ClassicAssert.IsNotNull(samRecipientStatus2, "There should be a status update for the sam");
            ClassicAssert.IsTrue(samRecipientStatus2.IsReadByRecipient);
            ClassicAssert.IsTrue(samRecipientStatus2.LatestTransferStatus == LatestTransferStatus.Delivered);
            ClassicAssert.IsTrue(samRecipientStatus2.LatestSuccessfullyDeliveredVersionTag == senderUploadResult2.NewVersionTag);
            
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
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

            ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            ClassicAssert.IsTrue(uploadResult.RecipientStatus[transitOptions.Recipients.Single()] == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(storageOptions.Drive);

            // validate recipient got the file

            var uploadResult1 = uploadResponse.Content;
            ClassicAssert.IsNotNull(uploadResult1);
            await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult1.File.TargetDrive);

            var recipientFiles = new Dictionary<string, SharedSecretEncryptedFileHeader>();
            foreach (var recipient in transitOptions.Recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.InitializedIdentities[recipient]);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(uploadResult1.GlobalTransitIdFileIdentifier);
                ClassicAssert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
                var file = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                ClassicAssert.IsNotNull(file);
                recipientFiles.Add(recipient, file);
            }


            return (uploadResult1, encryptedJsonContent64, recipientFiles);
        }

        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient, TargetDrive targetDrive,
            DrivePermission drivePermissions)
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

            ClassicAssert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

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

            ClassicAssert.IsTrue(senderDriveResponse.IsSuccessStatusCode);

            //
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = drivePermissions
            };

            var senderCircleId = Guid.NewGuid();
            var createCircleOnSenderResponse = await senderOwnerClient.Network.CreateCircle(senderCircleId,
                "Circle with drive access for the recipient to send back a read-receipt", new PermissionSetGrantRequest()
                {
                    Drives = new List<DriveGrantRequest>()
                    {
                        new()
                        {
                            PermissionedDrive = expectedPermissionedDrive
                        }
                    }
                });

            ClassicAssert.IsTrue(createCircleOnSenderResponse.IsSuccessStatusCode);


            var recipientCircleId = Guid.NewGuid();
            var createCircleOnRecipientResponse = await recipientOwnerClient.Network.CreateCircle(recipientCircleId, "Circle with drive access",
                new PermissionSetGrantRequest()
                {
                    Drives = new List<DriveGrantRequest>()
                    {
                        new()
                        {
                            PermissionedDrive = expectedPermissionedDrive
                        }
                    }
                });

            ClassicAssert.IsTrue(createCircleOnRecipientResponse.IsSuccessStatusCode);

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

            ClassicAssert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
            var senderConnectionInfo = getConnectionInfoResponse.Content;

            ClassicAssert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}