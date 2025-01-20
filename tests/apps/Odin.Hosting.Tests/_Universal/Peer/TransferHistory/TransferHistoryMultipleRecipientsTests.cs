using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests._Universal.Peer.TransferHistory
{
    public class TransferHistoryMultipleRecipientsTests
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

        public static IEnumerable OwnerAllowed()
        {
            yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        }

        public static IEnumerable AppAllowed()
        {
            yield return new object[]
            {
                new AppSpecifyDriveAccess(
                    TargetDrive.NewTargetDrive(),
                    DrivePermission.ReadWrite,
                    new TestPermissionKeyList(PermissionKeys.All.ToArray())),
                HttpStatusCode.OK
            };
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        [Ignore("wip")]
        public async Task CanReadTransferSummaryFromFileMixResults(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            List<TestIdentity> successRecipients =
            [
                TestIdentities.TomBombadil,
                TestIdentities.Samwise
            ];

            List<TestIdentity> failureRecipients =
            [
                TestIdentities.Collab,
                TestIdentities.Merry,
                TestIdentities.Pippin
            ];

            const DrivePermission drivePermissions = DrivePermission.Write;

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, successRecipients, targetDrive, drivePermissions);
            await PrepareScenario(senderOwnerClient, failureRecipients, targetDrive, drivePermissions);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = successRecipients.Select(r => r.OdinId.DomainName).ToList()
            };

            var (uploadResult, _, recipientFiles) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

            // send a read receipt
            foreach (var recipientFile in recipientFiles)
            {
                var recipient = recipientFile.Key;
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);

                //
                // Send the read receipt
                //
                var fileForReadReceipt = new ExternalFileIdentifier()
                {
                    FileId = recipientFile.Value.FileId,
                    TargetDrive = recipientFile.Value.TargetDrive
                };

                var sendReadReceiptResponse = await client.DriveRedux.SendReadReceipt([fileForReadReceipt]);
                Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
                var sendReadReceiptResult = sendReadReceiptResponse.Content;
                Assert.IsNotNull(sendReadReceiptResult);
                var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
                Assert.IsNotNull(item, "no record for file");
                var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
                Assert.IsNotNull(statusItem);
                Assert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

                await client.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);
            }

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive); // process all read receipts

            //
            // Assert: the sender has the transfer history updated
            //

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            var uploadedFileResponse1 = await driveClient.GetFileHeader(uploadResult.File);
            Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content;

            var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
            Assert.IsNotNull(summary, "missing transfer summary");
            Assert.IsTrue(summary.TotalDelivered == 1);
            Assert.IsTrue(summary.TotalReadByRecipient == 1);
            Assert.IsTrue(summary.TotalFailed == 0);
            Assert.IsTrue(summary.TotalInOutbox == 0);

            await this.DeleteScenario(senderOwnerClient, successRecipients);
            await this.DeleteScenario(senderOwnerClient, failureRecipients);
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        [Ignore("wip")]
        public async Task CanReadTransferHistoryForFileMixResults(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            List<TestIdentity> successRecipients =
            [
                TestIdentities.TomBombadil,
                TestIdentities.Samwise
            ];

            List<TestIdentity> failureRecipients =
            [
                TestIdentities.Collab,
                TestIdentities.Merry,
                TestIdentities.Pippin
            ];


            const DrivePermission circleDrivePermissions = DrivePermission.Write;

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, [TestIdentities.Samwise], targetDrive, circleDrivePermissions);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = successRecipients.Select(r => r.OdinId.DomainName).ToList()
            };

            var (uploadResult, _, recipientFiles) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

            // send a read receipt
            foreach (var recipientFile in recipientFiles)
            {
                var recipient = recipientFile.Key;
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);

                //
                // Send the read receipt
                //
                var fileForReadReceipt = new ExternalFileIdentifier()
                {
                    FileId = recipientFile.Value.FileId,
                    TargetDrive = recipientFile.Value.TargetDrive
                };

                var sendReadReceiptResponse = await client.DriveRedux.SendReadReceipt([fileForReadReceipt]);
                Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
                var sendReadReceiptResult = sendReadReceiptResponse.Content;
                Assert.IsNotNull(sendReadReceiptResult);
                var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
                Assert.IsNotNull(item, "no record for file");
                var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
                Assert.IsNotNull(statusItem);
                Assert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

                await client.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);
            }

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive); // process all read receipts

            //
            // Assert: the sender has the transfer history updated
            //

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            var historyResponse = await driveClient.GetTransferHistory(uploadResult.File);
            Assert.IsTrue(historyResponse.IsSuccessStatusCode, $"status code was {historyResponse.StatusCode}");

            foreach (var recipient in transitOptions.Recipients)
            {
                var theHistory = historyResponse.Content;
                Assert.IsTrue(theHistory.OriginalRecipientCount == transitOptions.Recipients.Count);
                Assert.IsTrue(theHistory.History.Results.Count == 1);
                var recipientStatus = theHistory.History.Results.SingleOrDefault(r => r.Recipient == recipient);
                Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                Assert.IsTrue(recipientStatus.IsReadByRecipient);
                Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
                Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }

            await this.DeleteScenario(senderOwnerClient, successRecipients);
            await this.DeleteScenario(senderOwnerClient, failureRecipients);
        }

        private async Task<(UploadResult response, string encryptedJsonContent64, Dictionary<string, SharedSecretEncryptedFileHeader>
                recipientFiles)>
            TransferEncryptedMetadata(
                OwnerApiClientRedux senderOwnerClient,
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
            Assert.IsNotNull(uploadResult1);

            var recipientFiles = new Dictionary<string, SharedSecretEncryptedFileHeader>();
            foreach (var recipient in transitOptions.Recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);
                await client.DriveRedux.ProcessInbox(storageOptions.Drive);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(uploadResult1.GlobalTransitIdFileIdentifier);
                Assert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
                var file = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                Assert.IsNotNull(file);
                recipientFiles.Add(recipient, file);
            }

            return (uploadResult1, encryptedJsonContent64, recipientFiles);
        }

        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, List<TestIdentity> recipients,
            TargetDrive targetDrive,
            DrivePermission drivePermissions)
        {
            // setup the recipients
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipients.FirstOrDefault());
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
            var createCircleOnRecipientResponse = await recipientOwnerClient.Network.CreateCircle(recipientCircleId,
                "Circle with drive access",
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

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, List<TestIdentity> identities)
        {
            foreach (var testIdentity in identities)
            {
                await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, testIdentity.OdinId);
            }
        }
    }
}