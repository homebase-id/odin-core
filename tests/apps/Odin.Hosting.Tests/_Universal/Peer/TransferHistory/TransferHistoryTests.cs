using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests._Universal.Peer.TransferHistory
{
    public class TransferHistoryTests
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
        public async Task ResendingFileKeepsOriginalRecipientCount(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles, originalKeyHeader, orignalUploadFileMetadata) =
                await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

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
                ClassicAssert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
                var sendReadReceiptResult = sendReadReceiptResponse.Content;
                ClassicAssert.IsNotNull(sendReadReceiptResult);
                var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
                ClassicAssert.IsNotNull(item, "no record for file");
                var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
                ClassicAssert.IsNotNull(statusItem);
                ClassicAssert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

                await client.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);
            }

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive); // process all read receipts

            // Validate the original recipient is set on the first upload
            var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(uploadResult.File);
            ClassicAssert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content;

            ClassicAssert.IsTrue(uploadedFile1.ServerMetadata.OriginalRecipientCount == transitOptions.Recipients.Count);

            //
            // now resend the file 
            //
            var newKeyHeader = new KeyHeader()
            {
                Iv = ByteArrayUtil.GetRndByteArray(16),
                AesKey = new SensitiveByteArray(originalKeyHeader.AesKey.GetKey())
            };

            orignalUploadFileMetadata.VersionTag = uploadResult.NewVersionTag;
            orignalUploadFileMetadata.IsEncrypted = true;

            var (updateFileResponse, _) = await senderOwnerClient.DriveRedux.UpdateExistingEncryptedMetadata(uploadResult.File,
                newKeyHeader,
                orignalUploadFileMetadata);
            ClassicAssert.IsTrue(updateFileResponse.IsSuccessStatusCode);

            //
            // Assert: recipient count is still set
            //

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());
            var updatedFileResponse = await driveClient.GetFileHeader(uploadResult.File);
            ClassicAssert.IsTrue(updatedFileResponse.IsSuccessStatusCode);
            var updatedFile = updatedFileResponse.Content;
            ClassicAssert.IsTrue(updatedFile.ServerMetadata.OriginalRecipientCount == transitOptions.Recipients.Count);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        public async Task CanReadTransferSummaryFromFile(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles, _, _) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

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
                ClassicAssert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
                var sendReadReceiptResult = sendReadReceiptResponse.Content;
                ClassicAssert.IsNotNull(sendReadReceiptResult);
                var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
                ClassicAssert.IsNotNull(item, "no record for file");
                var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
                ClassicAssert.IsNotNull(statusItem);
                ClassicAssert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

                await client.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);
            }

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive); // process all read receipts

            //
            // Assert: the sender has the transfer history updated
            //

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            var uploadedFileResponse1 = await driveClient.GetFileHeader(uploadResult.File);
            ClassicAssert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content;

            ClassicAssert.IsTrue(uploadedFile1.ServerMetadata.OriginalRecipientCount == transitOptions.Recipients.Count);

            var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
            ClassicAssert.IsNotNull(summary, "missing transfer summary");
            ClassicAssert.IsTrue(summary.TotalDelivered == 1);
            ClassicAssert.IsTrue(summary.TotalReadByRecipient == 1);
            ClassicAssert.IsTrue(summary.TotalFailed == 0);
            ClassicAssert.IsTrue(summary.TotalInOutbox == 0);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        public async Task CanReadTransferSummaryFromQueryBatch(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles, _, _) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

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
                ClassicAssert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
                var sendReadReceiptResult = sendReadReceiptResponse.Content;
                ClassicAssert.IsNotNull(sendReadReceiptResult);
                var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
                ClassicAssert.IsNotNull(item, "no record for file");
                var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
                ClassicAssert.IsNotNull(statusItem);
                ClassicAssert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

                await client.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);
            }

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive); // process all read receipts

            //
            // Assert: the sender has the transfer history updated
            //

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            var q = new QueryBatchRequest
            {
                QueryParams = new()
                {
                    TargetDrive = targetDrive,
                    FileState = [FileState.Active]
                },
                ResultOptionsRequest = new()
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true,
                    IncludeTransferHistory = true
                }
            };

            var uploadedFileResponse1 = await driveClient.QueryBatch(q);
            ClassicAssert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content.SearchResults.FirstOrDefault();
            ClassicAssert.IsNotNull(uploadedFile1);

            var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
            ClassicAssert.IsNotNull(summary, "missing transfer summary");
            ClassicAssert.IsTrue(summary.TotalDelivered == 1);
            ClassicAssert.IsTrue(summary.TotalReadByRecipient == 1);
            ClassicAssert.IsTrue(summary.TotalFailed == 0);
            ClassicAssert.IsTrue(summary.TotalInOutbox == 0);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        public async Task CanReadTransferSummaryFromQueryModified(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles, _, _) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

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
                ClassicAssert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
                var sendReadReceiptResult = sendReadReceiptResponse.Content;
                ClassicAssert.IsNotNull(sendReadReceiptResult);
                var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
                ClassicAssert.IsNotNull(item, "no record for file");
                var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
                ClassicAssert.IsNotNull(statusItem);
                ClassicAssert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

                await client.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);
            }

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive); // process all read receipts

            //
            // Assert: the sender has the transfer history updated
            //

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            var q = new QueryModifiedRequest()
            {
                QueryParams = new()
                {
                    TargetDrive = targetDrive,
                    FileState = [FileState.Active]
                },
                ResultOptions = new()
                {
                    MaxRecords = 10,
                    IncludeTransferHistory = true
                }
            };

            var uploadedFileResponse1 = await driveClient.QueryModified(q);
            ClassicAssert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content.SearchResults.FirstOrDefault();
            ClassicAssert.IsNotNull(uploadedFile1);

            var summary = uploadedFile1.ServerMetadata.TransferHistory.Summary;
            ClassicAssert.IsNotNull(summary, "missing transfer summary");
            ClassicAssert.IsTrue(summary.TotalDelivered == 1);
            ClassicAssert.IsTrue(summary.TotalReadByRecipient == 1);
            ClassicAssert.IsTrue(summary.TotalFailed == 0);
            ClassicAssert.IsTrue(summary.TotalInOutbox == 0);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        [TestCaseSource(nameof(OwnerAllowed))]
        [TestCaseSource(nameof(AppAllowed))]
        public async Task CanReadTransferHistoryForFile(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission circleDrivePermissions = DrivePermission.Write;

            //
            // Setup
            //
            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, circleDrivePermissions);

            //
            // Act transfer file, then send read receipt
            //
            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResult, _, recipientFiles, _, _) = await TransferEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

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
                ClassicAssert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
                var sendReadReceiptResult = sendReadReceiptResponse.Content;
                ClassicAssert.IsNotNull(sendReadReceiptResult);
                var item = sendReadReceiptResult.Results.SingleOrDefault(d => d.File == fileForReadReceipt);
                ClassicAssert.IsNotNull(item, "no record for file");
                var statusItem = item.Status.SingleOrDefault(i => i.Recipient == senderOwnerClient.Identity.OdinId);
                ClassicAssert.IsNotNull(statusItem);
                ClassicAssert.IsTrue(statusItem.Status == SendReadReceiptResultStatus.Enqueued);

                await client.DriveRedux.WaitForEmptyOutbox(fileForReadReceipt.TargetDrive);
            }

            await senderOwnerClient.DriveRedux.ProcessInbox(targetDrive); // process all read receipts

            //
            // Assert: the sender has the transfer history updated
            //

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            var historyResponse = await driveClient.GetTransferHistory(uploadResult.File);
            ClassicAssert.IsTrue(historyResponse.IsSuccessStatusCode, $"status code was {historyResponse.StatusCode}");

            foreach (var recipient in transitOptions.Recipients)
            {
                var theHistory = historyResponse.Content;
                ClassicAssert.IsTrue(theHistory.OriginalRecipientCount == transitOptions.Recipients.Count);
                ClassicAssert.IsTrue(theHistory.History.Results.Count == 1);
                var recipientStatus = theHistory.History.Results.SingleOrDefault(r => r.Recipient == recipient);
                ClassicAssert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                ClassicAssert.IsTrue(recipientStatus.IsReadByRecipient);
                ClassicAssert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
                ClassicAssert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        private async Task<(
                UploadResult response,
                string encryptedJsonContent64,
                Dictionary<string, SharedSecretEncryptedFileHeader> recipientFiles,
                KeyHeader originalKeyHeader,
                UploadFileMetadata uploadFileMetadata)>
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

            var keyHeader = KeyHeader.NewRandom16();
            var (uploadResponse, encryptedJsonContent64) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions,
                keyHeader
            );

            ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            ClassicAssert.IsTrue(uploadResult!.RecipientStatus.Count == 1);
            ClassicAssert.IsTrue(uploadResult.RecipientStatus[transitOptions.Recipients.Single()] == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(storageOptions.Drive);

            // validate recipient got the file

            var uploadResult1 = uploadResponse.Content;
            ClassicAssert.IsNotNull(uploadResult1);

            var recipientFiles = new Dictionary<string, SharedSecretEncryptedFileHeader>();
            foreach (var recipient in transitOptions.Recipients)
            {
                var client = _scaffold.CreateOwnerApiClientRedux(TestIdentities.All[recipient]);
                await client.DriveRedux.ProcessInbox(storageOptions.Drive);
                var recipientFileResponse = await client.DriveRedux.QueryByGlobalTransitId(uploadResult1.GlobalTransitIdFileIdentifier);
                ClassicAssert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
                var file = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                ClassicAssert.IsNotNull(file);
                recipientFiles.Add(recipient, file);
            }

            return (uploadResult1, encryptedJsonContent64, recipientFiles, keyHeader, fileMetadata);
        }

        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
            TargetDrive targetDrive,
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