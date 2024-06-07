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
using Refit;

namespace Odin.Hosting.Tests._Universal.Peer
{
    public class PeerReadReceiptTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
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


        /*
         * Successes
         * CanSendReadReceipt
         *  - send file; send read receipt; sender can see read-receipt updated
         * CanSendMultipleReadReceipts
         *  - send multiple files; send multiple read receipts (partial of files); sender can see read receipts
         *
         * Failures
         * SendReadReceiptWhenRecipientDoesNotHaveWriteAccessToOriginalSendersDrive
         *  send file; caller does not have write access to sender's drive to update read receipt
         *  - send error code back to calling app
         *
         * SendReadReceiptWhenOriginalSenderIdentityIsNotResponding
         *  send file; recipient gets file; send read receipt - original-sender's server does not respond
         *  - send error code back to calling app
         *
         * SendReadReceiptWhenNotConnected (can happen after your disconnect from someone)
         *  - send error code back to calling app
         *
         */

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
                Recipients = [recipientOwnerClient.Identity.OdinId],
                Schedule = ScheduleOptions.SendAsync
            };

            var (uploadResponse, _, recipientFiles) =
                await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientOwnerClient, targetDrive, transitOptions);


            var uploadResult = uploadResponse.Content;
            await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult.File.TargetDrive);

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(recipientOwnerClient.Identity.OdinId, callerContext.GetFactory());

            //
            // Send the read receipt
            //
            var sendReadReceiptResponse = await driveClient.SendReadReceipt([
                new ExternalFileIdentifier()
                {
                    FileId = recipientFiles.Single().Value.FileId,
                    TargetDrive = recipientFiles.Single().Value.TargetDrive
                }
            ]);

            Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
            var sendReadReceiptResult = sendReadReceiptResponse.Content;
            Assert.IsNotNull(sendReadReceiptResult);

            Assert.IsTrue(sendReadReceiptResult.Results.TryGetValue(senderOwnerClient.Identity.OdinId, out var value));
            Assert.IsTrue(value == SendReadReceiptResultStatus.RequestAcceptedIntoInbox);

            //
            // Assert the read receipt was updated on the sender's file
            //

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive);

            var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(uploadResult.File);
            Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content;

            Assert.IsTrue(
                uploadedFile1.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipientOwnerClient.Identity.OdinId, out var recipientStatus));
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsTrue(recipientStatus.IsReadByRecipient);
            Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
            Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public Task CanSendMultipleReadReceipts(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            Assert.Inconclusive("todo");
            return Task.CompletedTask;
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        // public async Task CanSendMultipleReadReceiptsAcrossMultipleRecipient(IApiClientContext callerContext,
        //     HttpStatusCode expectedStatusCode)
        // {
        //     var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        //     var recipientSamOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        //     var recipientPippinOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        //
        //     const DrivePermission drivePermissions = DrivePermission.Write;
        //
        //     var targetDrive = callerContext.TargetDrive;
        //     await PrepareScenario(senderOwnerClient, recipientSamOwnerClient, targetDrive, drivePermissions);
        //
        //     // send to sam
        //     var (bothUploadResponse1, _, bothRecipientFiles) = await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientSamOwnerClient, targetDrive,
        //         new TransitOptions()
        //         {
        //             Recipients = [recipientSamOwnerClient.Identity.OdinId, recipientPippinOwnerClient.Identity.OdinId],
        //             Schedule = ScheduleOptions.SendAsync
        //         });
        //
        //     var (samUploadResponse2, _, samRecipientFile2) = await AssertCanUploadEncryptedMetadata(senderOwnerClient, recipientSamOwnerClient, targetDrive,
        //         new TransitOptions()
        //         {
        //             Recipients = [recipientSamOwnerClient.Identity.OdinId],
        //             Schedule = ScheduleOptions.SendAsync
        //         });
        //
        //     await callerContext.Initialize(senderOwnerClient);
        //     var samDriveClient = new UniversalDriveApiClient(recipientSamOwnerClient.Identity.OdinId, callerContext.GetFactory());
        //
        //     //
        //     // Sam Sends the read receipt
        //     //
        //     var samSendReadReceiptResponse = await samDriveClient.SendReadReceipt([
        //         new ExternalFileIdentifier()
        //         {
        //             FileId = bothRecipientFiles[recipientSamOwnerClient.Identity.OdinId].FileId,
        //             TargetDrive = bothRecipientFiles[recipientSamOwnerClient.Identity.OdinId].TargetDrive
        //         },
        //         new ExternalFileIdentifier()
        //         {
        //             FileId = samRecipientFile2[recipientSamOwnerClient.Identity.OdinId].FileId,
        //             TargetDrive = samRecipientFile2[recipientSamOwnerClient.Identity.OdinId].TargetDrive
        //         }
        //     ]);
        //     
        //     Assert.IsTrue(samSendReadReceiptResponse.IsSuccessStatusCode);
        //     var samSendReadReceiptResult = samSendReadReceiptResponse.Content;
        //     Assert.IsNotNull(samSendReadReceiptResult);
        //
        //     Assert.IsTrue(samSendReadReceiptResult.Results.TryGetValue(senderOwnerClient.Identity.OdinId, out var samValue));
        //     Assert.IsTrue(samValue == SendReadReceiptResultStatus.RequestAcceptedIntoInbox);
        //
        //     //
        //     // Pippin Sends the read receipt
        //     //
        //     var pippinSendReadReceiptResponse = await samDriveClient.SendReadReceipt([
        //         new ExternalFileIdentifier()
        //         {
        //             FileId = bothRecipientFiles[recipientPippinOwnerClient.Identity.OdinId].FileId,
        //             TargetDrive = bothRecipientFiles[recipientPippinOwnerClient.Identity.OdinId].TargetDrive
        //         },
        //     ]);
        //
        //     Assert.IsTrue(pippinSendReadReceiptResponse.IsSuccessStatusCode);
        //     var pippinSendReadReceiptResult = pippinSendReadReceiptResponse.Content;
        //     Assert.IsNotNull(pippinSendReadReceiptResult);
        //
        //     Assert.IsTrue(pippinSendReadReceiptResult.Results.TryGetValue(senderOwnerClient.Identity.OdinId, out var pippinValue));
        //     Assert.IsTrue(pippinValue == SendReadReceiptResultStatus.RequestAcceptedIntoInbox);
        //
        //     //
        //     // Assert the read receipt was updated on the sender's file
        //     //
        //
        //     await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive);
        //
        //     var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(bothUploadResponse1.Content.File);
        //     Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
        //     var uploadedFile1 = uploadedFileResponse1.Content;
        //
        //     var file1TransferHistory = uploadedFile1.ServerMetadata.TransferHistory;
        //     Assert.IsTrue(file1TransferHistory.Recipients.Count == 2);
        //     Assert.IsTrue(file1TransferHistory.Recipients.TryGetValue(recipientSamOwnerClient.Identity.OdinId, out var samRecipientStatus));
        //     Assert.IsNotNull(samRecipientStatus, "There should be a status update for the sam");
        //     Assert.IsTrue(samRecipientStatus.IsReadByRecipient);
        //     Assert.IsTrue(samRecipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
        //     Assert.IsTrue(samRecipientStatus.LatestSuccessfullyDeliveredVersionTag == bothUploadResponse1.Content.NewVersionTag);
        //
        //     Assert.IsTrue(file1TransferHistory.Recipients.TryGetValue(recipientPippinOwnerClient.Identity.OdinId, out var pippinRecipientStatus));
        //     Assert.IsNotNull(pippinRecipientStatus, "There should be a status update for the pippin");
        //     Assert.IsTrue(pippinRecipientStatus.IsReadByRecipient);
        //     Assert.IsTrue(pippinRecipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
        //     Assert.IsTrue(pippinRecipientStatus.LatestSuccessfullyDeliveredVersionTag == bothUploadResponse1.Content.NewVersionTag);
        //
        //     var uploadedFileResponse2 = await senderOwnerClient.DriveRedux.GetFileHeader(bothUploadResponse1.Content.File);
        //     Assert.IsTrue(uploadedFileResponse2.IsSuccessStatusCode);
        //     var uploadedFile2 = uploadedFileResponse2.Content;
        //
        //     var file1TransferHistory2 = uploadedFile2.ServerMetadata.TransferHistory;
        //     Assert.IsTrue(file1TransferHistory2.Recipients.Count == 2);
        //     Assert.IsTrue(file1TransferHistory2.Recipients.TryGetValue(recipientSamOwnerClient.Identity.OdinId, out var samRecipientStatus));
        //     Assert.IsNotNull(samRecipientStatus, "There should be a status update for the recipient");
        //     Assert.IsTrue(samRecipientStatus.IsReadByRecipient);
        //     Assert.IsTrue(samRecipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
        //     Assert.IsTrue(samRecipientStatus.LatestSuccessfullyDeliveredVersionTag == bothUploadResponse1.Content.NewVersionTag);
        //
        //     await this.DeleteScenario(senderOwnerClient, recipientSamOwnerClient);
        // }

        private async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64, Dictionary<string, SharedSecretEncryptedFileHeader>
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
            var uploadResult = uploadResponse.Content;
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            Assert.IsTrue(uploadResult.RecipientStatus[transitOptions.Recipients.Single()] == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(storageOptions.Drive);

            // validate recipient got the file

            var uploadResult1 = uploadResponse.Content;
            await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult1.File.TargetDrive);

            var recipientFiles = new Dictionary<string, SharedSecretEncryptedFileHeader>();
            foreach (var recipient in transitOptions.Recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(uploadResult1.GlobalTransitIdFileIdentifier);
                Assert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
                var file = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                Assert.IsNotNull(file);
                recipientFiles.Add(recipient, file);
            }


            return (uploadResponse, encryptedJsonContent64, recipientFiles);
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

            Assert.IsTrue(createCircleOnSenderResponse.IsSuccessStatusCode);


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
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}