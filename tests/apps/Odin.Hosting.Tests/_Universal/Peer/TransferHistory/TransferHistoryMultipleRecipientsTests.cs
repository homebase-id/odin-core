using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using NUnit.Framework;
using Odin.Core.Identity;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
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
        public async Task CanReadTransferSummaryFromFileMixResultsWhenSourceFileDoesNotAllowDistribution(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            await senderOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

            List<TestIdentity> connectedRecipients =
            [
                TestIdentities.TomBombadil,
                TestIdentities.Samwise
            ];

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            var senderCircleId = await PrepareSender(senderOwnerClient, targetDrive, DrivePermission.Write);
            await ConnectRecipientsToSender(senderOwnerClient, connectedRecipients, targetDrive, DrivePermission.Write, senderCircleId);

            //
            // Act: transfer the file then 
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = connectedRecipients.Select(t => t.OdinId.DomainName).ToList()
            };

            var (uploadResult, _) = await TransferEncryptedMetadata(
                senderOwnerClient, targetDrive, transitOptions, allowDistribution: false);

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
            Assert.IsTrue(summary.TotalDelivered == 0);
            Assert.IsTrue(summary.TotalReadByRecipient == 0);
            Assert.IsTrue(summary.TotalFailed == connectedRecipients.Count);
            Assert.IsTrue(summary.TotalInOutbox == connectedRecipients.Count);

            await this.DeleteScenario(senderOwnerClient, connectedRecipients);
            // await this.DeleteScenario(senderOwnerClient, disconnectedRecipients);
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        public async Task CanReadTransferSummaryFromFileMixResultsWhenRecipientReturnsAccessDenied(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            await senderOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

            List<TestIdentity> connectedRecipients =
            [
                TestIdentities.TomBombadil,
                TestIdentities.Samwise
            ];

            List<TestIdentity> disconnectedRecipients =
            [
                TestIdentities.Collab,
                TestIdentities.Merry,
                TestIdentities.Pippin
            ];

            var allRecipients = connectedRecipients.ToList();
            allRecipients.AddRange(disconnectedRecipients.ToList());

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            var senderCircleId = await PrepareSender(senderOwnerClient, targetDrive, DrivePermission.Write);
            await ConnectRecipientsToSender(senderOwnerClient, allRecipients, targetDrive, DrivePermission.Write, senderCircleId);
            await RecipientsToDisconnectFromSender(disconnectedRecipients, senderOwnerClient.OdinId);

            //
            // Act: transfer the file then 
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = allRecipients.Select(r => r.OdinId.DomainName).ToList()
            };

            var (uploadResult, _) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

            // send a read receipt
            foreach (var recipient in connectedRecipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(recipient);

                await client.DriveRedux.ProcessInbox(targetDrive);

                // get the file for the recipient
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
                Assert.IsTrue(recipientFileResponse.IsSuccessStatusCode);

                var recipientFile = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                Assert.IsNotNull(recipientFile);

                //
                // Send the read receipt
                //
                var fileForReadReceipt = new ExternalFileIdentifier()
                {
                    FileId = recipientFile.FileId,
                    TargetDrive = recipientFile.TargetDrive
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
            Assert.IsTrue(summary.TotalDelivered == connectedRecipients.Count);
            Assert.IsTrue(summary.TotalReadByRecipient == connectedRecipients.Count);
            Assert.IsTrue(summary.TotalFailed == disconnectedRecipients.Count);
            Assert.IsTrue(summary.TotalInOutbox == 0);

            await this.DeleteScenario(senderOwnerClient, connectedRecipients);
            // await this.DeleteScenario(senderOwnerClient, disconnectedRecipients);
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        public async Task CanReadTransferHistoryForFileMixResults(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            await senderOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

            List<TestIdentity> connectedRecipients =
            [
                TestIdentities.TomBombadil,
                TestIdentities.Samwise
            ];

            List<TestIdentity> disconnectedRecipients =
            [
                TestIdentities.Collab,
                TestIdentities.Merry,
                TestIdentities.Pippin
            ];

            var allRecipients = connectedRecipients.ToList();
            allRecipients.AddRange(disconnectedRecipients);

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            var senderCircleId = await PrepareSender(senderOwnerClient, targetDrive, DrivePermission.Write);
            await ConnectRecipientsToSender(senderOwnerClient, allRecipients, targetDrive, DrivePermission.Write, senderCircleId);
            await RecipientsToDisconnectFromSender(disconnectedRecipients, senderOwnerClient.OdinId);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = allRecipients.Select(t => t.OdinId.DomainName).ToList()
            };

            var (uploadResult, _) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

            // send a read receipt
            foreach (var recipient in connectedRecipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(recipient);

                await client.DriveRedux.ProcessInbox(targetDrive);

                // get the file for the recipient
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
                Assert.IsTrue(recipientFileResponse.IsSuccessStatusCode);

                var recipientFile = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                Assert.IsNotNull(recipientFile);

                //
                // Send the read receipt
                //
                var fileForReadReceipt = new ExternalFileIdentifier()
                {
                    FileId = recipientFile.FileId,
                    TargetDrive = recipientFile.TargetDrive
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

            var theHistory = historyResponse.Content;
            Assert.IsTrue(theHistory.OriginalRecipientCount == transitOptions.Recipients.Count);
            Assert.IsTrue(theHistory.History.Results.Count == transitOptions.Recipients.Count);

            foreach (var recipient in connectedRecipients)
            {
                var recipientStatus = theHistory.History.Results.SingleOrDefault(r => r.Recipient == recipient.OdinId);
                Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                Assert.IsTrue(recipientStatus.IsReadByRecipient);
                Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
                Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }

            foreach (var recipient in disconnectedRecipients)
            {
                var recipientStatus = theHistory.History.Results.SingleOrDefault(r => r.Recipient == recipient.OdinId);
                Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                Assert.IsTrue(recipientStatus.IsReadByRecipient == false);
                Assert.IsTrue(recipientStatus.IsInOutbox == false);
                Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.RecipientIdentityReturnedAccessDenied);
                Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == null);
            }

            await this.DeleteScenario(senderOwnerClient, connectedRecipients);
        }

        private async Task<(UploadResult response, string encryptedJsonContent64)>
            TransferEncryptedMetadata(
                OwnerApiClientRedux senderOwnerClient,
                TargetDrive targetDrive,
                TransitOptions transitOptions,
                bool allowDistribution = true)
        {
            const string uploadedContent = "pie";

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = allowDistribution,
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
            Assert.IsTrue(uploadResult!.RecipientStatus.Count == transitOptions.Recipients.Count);

            if (allowDistribution) //we will only have a chance to get an empty outbox if the file is distributed
            {
                await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(storageOptions.Drive);
            }

            var uploadResult1 = uploadResponse.Content;
            Assert.IsNotNull(uploadResult1);

            return (uploadResult1, encryptedJsonContent64);
        }

        private async Task RecipientsToDisconnectFromSender(List<TestIdentity> recipients, OdinId sender)
        {
            foreach (var recipient in recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(recipient);
                await client.Connections.DisconnectFrom(sender);
            }
        }

        private async Task ConnectRecipientsToSender(OwnerApiClientRedux senderOwnerClient, List<TestIdentity> recipients,
            TargetDrive targetDrive,
            DrivePermission drivePermission,
            Guid senderCircleId)
        {
            foreach (var recipient in recipients)
            {
                // setup the recipients
                var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);
                await recipientOwnerClient.Configuration.DisableAutoAcceptIntroductions(true);

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

                var recipientCircleId = Guid.NewGuid();
                var createCircleOnRecipientResponse = await recipientOwnerClient.Network.CreateCircle(recipientCircleId,
                    "Circle with drive access",
                    new PermissionSetGrantRequest()
                    {
                        Drives = new List<DriveGrantRequest>()
                        {
                            new()
                            {
                                PermissionedDrive = new()
                                {
                                    Drive = targetDrive,
                                    Permission = drivePermission
                                }
                            }
                        }
                    });

                Assert.IsTrue(createCircleOnRecipientResponse.IsSuccessStatusCode);

                // get connected
                await recipientOwnerClient.Connections.SendConnectionRequest(senderOwnerClient.Identity.OdinId, [recipientCircleId]);
                var acceptResponse =
                    await senderOwnerClient.Connections.AcceptConnectionRequest(recipientOwnerClient.Identity.OdinId, [senderCircleId]);
                Assert.IsTrue(acceptResponse.IsSuccessStatusCode, $"Status code was {acceptResponse.StatusCode}");
            }
        }

        private async Task<Guid> PrepareSender(OwnerApiClientRedux senderOwnerClient,
            TargetDrive targetDrive,
            DrivePermission drivePermissions)
        {
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
            return senderCircleId;
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