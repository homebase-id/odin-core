using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Metadata;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.DriveTests.Outbox
{
    public class OutboxProcessingTestsMultipleRecipients
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);

            var overrides = new Dictionary<string, string>()
            {
                { "Job:Enabled", bool.TrueString }
            };
            _scaffold.RunBeforeAnyTests(envOverrides: overrides);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task SenderCanReceiveHttpAcceptedStatusCodeWhenTransferringFileMultipleRecipients()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
            var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

            // List<OwnerApiClientRedux> recipients = [sam, pippin];
            List<OwnerApiClientRedux> recipients = [sam];

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipients, targetDrive, drivePermissions);

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

            var transitOptions = new TransitOptions()
            {
                Recipients = recipients.Select(r => r.Identity.OdinId.ToString()).ToList(),
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendNowAwaitResponse,
                RemoteTargetDrive = default
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );
            
            foreach (var recipient in recipients)
            {
                Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
                Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.Accepted);
                var uploadResult = uploadResponse.Content;
                Assert.IsTrue(uploadResult.RecipientStatus.Count == recipients.Count);
                Assert.IsTrue(uploadResult.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Queued);
            }

            await this.DeleteScenario(senderOwnerClient, recipients);
        }

        [Test]
        public async Task RecipientTransferHistoryOnSenderIsUpdatedWhenTransferringFile()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
            var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

            List<OwnerApiClientRedux> recipients = [sam, pippin];

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipients, targetDrive, drivePermissions);

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

            var transitOptions = new TransitOptions()
            {
                Recipients = recipients.Select(r => r.Identity.OdinId.ToString()).ToList(),
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendNowAwaitResponse,
                RemoteTargetDrive = default
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            //hack wait for outbox
            await Task.Delay(5000);
            
            foreach (var recipient in recipients)
            {
                Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
                Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.Accepted);
                var uploadResult = uploadResponse.Content;
                Assert.IsTrue(uploadResult.RecipientStatus.Count == recipients.Count);
                Assert.IsTrue(uploadResult.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Queued);

                // Assert: file that was sent has peer transfer status updated
                var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(uploadResult.File);
                Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
                var uploadedFile1 = uploadedFileResponse1.Content;

                Assert.IsTrue(
                    uploadedFile1.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipient.Identity.OdinId, out var recipientStatus));
                Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                Assert.IsNull(recipientStatus.LatestProblemStatus);
                Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }

            //TODO: revisit when michael's outbox code is present
            // Process the outbox

            // senderOwnerClient.DriveRedux.ProcessOutbox()
            // check the file again

            await this.DeleteScenario(senderOwnerClient, recipients);
        }

        [Test]
        public async Task GetModifiedOfSenderFilesIncludesFilesWithUpdatedPeerTransferStatusAndCanExcludeRecipientTransferHistory()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
            var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

            List<OwnerApiClientRedux> recipients = [sam, pippin];

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipients, targetDrive, drivePermissions);

            const string uploadedContent = "pie";

            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = true,
                AppData = new()
                {
                    Content = uploadedContent,
                    FileType = 1011,
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
                Recipients = recipients.Select(r => r.Identity.OdinId.ToString()).ToList(),
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendNowAwaitResponse,
                RemoteTargetDrive = default
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            //wait for outbox processing
            await Task.Delay(4000);

            foreach (var recipient in recipients)
            {
                Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
                Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.Accepted);
                var uploadResult = uploadResponse.Content;
                Assert.IsTrue(uploadResult.RecipientStatus.Count == recipients.Count);
                Assert.IsTrue(uploadResult.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Queued);

                //Get modified to results ensure it will show up after a transfer
                var queryModifiedResponse = await senderOwnerClient.DriveRedux.QueryModified(new QueryModifiedRequest()
                {
                    QueryParams = new()
                    {
                        TargetDrive = targetDrive,
                        FileType = [fileMetadata.AppData.FileType]
                    },
                    ResultOptions = new QueryModifiedResultOptions()
                    {
                        MaxDate = UnixTimeUtc.Now().AddSeconds(-100).milliseconds
                    }
                });

                Assert.IsTrue(queryModifiedResponse.IsSuccessStatusCode);
                var modifiedResults = queryModifiedResponse.Content;
                var fileInResults = modifiedResults.SearchResults.SingleOrDefault(r => r.FileId == uploadResult.File.FileId);
                Assert.IsNotNull(fileInResults);

                Assert.IsTrue(fileInResults.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipient.Identity.OdinId,
                    out var statusFromGetModifiedResults));
                Assert.IsNotNull(statusFromGetModifiedResults, "There should be a status update for the recipient");
                Assert.IsNull(statusFromGetModifiedResults.LatestProblemStatus);
                Assert.IsTrue(statusFromGetModifiedResults.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }

            //TODO: revisit when michael's outbox code is present
            // Process the outbox

            // senderOwnerClient.DriveRedux.ProcessOutbox()
            // check the file again

            await this.DeleteScenario(senderOwnerClient, recipients);
        }

        [Test]
        public async Task GetBatchOnSenderCanExcludeRecipientTransferHistory()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);

            var sam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
            var pippin = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Pippin);

            List<OwnerApiClientRedux> recipients = [sam, pippin];

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipients, targetDrive, drivePermissions);

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

            var transitOptions = new TransitOptions()
            {
                Recipients = recipients.Select(r => r.Identity.OdinId.ToString()).ToList(),
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendNowAwaitResponse,
                RemoteTargetDrive = default
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );
            
            await Task.Delay(10000);

            foreach (var recipient in recipients)
            {
                Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
                Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.Accepted);
                var uploadResult = uploadResponse.Content;
                Assert.IsTrue(uploadResult.RecipientStatus.Count == recipients.Count);
                Assert.IsTrue(uploadResult.RecipientStatus[recipient.Identity.OdinId] == TransferStatus.Queued);

                // Assert: file that was sent has peer transfer status updated
                var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(uploadResult.File);
                Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
                var uploadedFile1 = uploadedFileResponse1.Content;

                Assert.IsTrue(uploadedFile1.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipient.Identity.OdinId, out var recipientStatus));
                Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                Assert.IsNull(recipientStatus.LatestProblemStatus);
                Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }
            //TODO: revisit when michael's outbox code is present
            // Process the outbox

            // senderOwnerClient.DriveRedux.ProcessOutbox()
            // check the file again

            await this.DeleteScenario(senderOwnerClient, recipients);
        }

        private async Task PrepareScenario(OwnerApiClientRedux senderOwnerClient, List<OwnerApiClientRedux> recipients, TargetDrive targetDrive,
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

            foreach (var recipient in recipients)
            {
                //
                // Sender sends connection request
                //
                await senderOwnerClient.Connections.SendConnectionRequest(recipient.Identity.OdinId, new List<GuidId>());
                await SetupRecipient(recipient, senderOwnerClient.Identity.OdinId, targetDrive, drivePermissions);
            }
        }

        private static async Task SetupRecipient(OwnerApiClientRedux recipient, OdinId sender, TargetDrive targetDrive,
            DrivePermission drivePermissions)
        {
            //
            // Recipient creates a target drive
            //
            var recipientDriveResponse = await recipient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            Assert.IsTrue(recipientDriveResponse.IsSuccessStatusCode);

            //
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = drivePermissions
            };

            var circleId = Guid.NewGuid();
            var createCircleResponse = await recipient.Network.CreateCircle(circleId, "Circle with drive access", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            Assert.IsTrue(createCircleResponse.IsSuccessStatusCode);


            //
            // Recipient accepts; grants access to circle
            //
            await recipient.Connections.AcceptConnectionRequest(sender, new List<GuidId>() { circleId });

            // 
            // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var getConnectionInfoResponse = await recipient.Network.GetConnectionInfo(sender);

            Assert.IsTrue(getConnectionInfoResponse.IsSuccessStatusCode);
            var senderConnectionInfo = getConnectionInfoResponse.Content;

            Assert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == expectedPermissionedDrive)));
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, List<OwnerApiClientRedux> recipients)
        {
            foreach (var recipient in recipients)
            {
                await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipient.Identity.OdinId);
            }
        }
    }
}