using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Time;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests._Universal.Outbox
{
    public class OutboxProcessingSingleRecipientTests
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

        public static IEnumerable TestCases()
        {
            // yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
            // yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
            yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RecipientTransferHistoryOnSenderIsUpdatedWhenTransferringFileToSingleRecipient(IApiClientContext callerContext,
            HttpStatusCode expectedStatusCode)
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = callerContext.TargetDrive;
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

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
                Recipients = [recipientOwnerClient.Identity.OdinId],
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendAsync
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
            Assert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            await driveClient.WaitForEmptyOutbox(targetDrive);

            if (expectedStatusCode == HttpStatusCode.OK)
            {
                // validate recipient got the file

                await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult.File.TargetDrive);

                var recipientFileResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
                Assert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
                var recipientFile = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                Assert.IsNotNull(recipientFile);
                Assert.IsTrue(recipientFile.FileMetadata.AppData.Content == encryptedJsonContent64);

                //
                // Validate the transfer history was updated correctly
                //
                var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(uploadResult.File);
                Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
                var uploadedFile1 = uploadedFileResponse1.Content;

                Assert.IsTrue(
                    uploadedFile1.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipientOwnerClient.Identity.OdinId, out var recipientStatus));
                Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                Assert.IsFalse(recipientStatus.IsInOutbox);
                Assert.IsFalse(recipientStatus.IsReadByRecipient);
                Assert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
                Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task GetModifiedOfSenderFilesIncludesFilesWithUpdatedPeerTransferStatusAndCanExcludeRecipientTransferHistory()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

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
                Recipients = [recipientOwnerClient.Identity.OdinId],
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendAsync,
                // Priority = PriorityOptions.High,
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

            Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            Assert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

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

            Assert.IsTrue(fileInResults.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipientOwnerClient.Identity.OdinId,
                out var statusFromGetModifiedResults));
            Assert.IsNotNull(statusFromGetModifiedResults, "There should be a status update for the recipient");
            Assert.IsTrue(statusFromGetModifiedResults.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            Assert.IsTrue(statusFromGetModifiedResults.LatestTransferStatus == LatestTransferStatus.Delivered);
            Assert.IsFalse(statusFromGetModifiedResults.IsInOutbox);
            Assert.IsFalse(statusFromGetModifiedResults.IsReadByRecipient);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task GetBatchOnSenderCanExcludeRecipientTransferHistory()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            const string uploadedContent = "pie";
            const int fileType = 1033;
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = true,
                AppData = new()
                {
                    Content = uploadedContent,
                    FileType = fileType,
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
                Recipients = [recipientOwnerClient.Identity.OdinId],
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendAsync
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

            Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            Assert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            // Assert: file that was sent has peer transfer status updated
            var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(uploadResult.File);
            Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content;

            Assert.IsTrue(uploadedFile1.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipientOwnerClient.Identity.OdinId, out var recipientStatus));
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

            var request = new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = targetDrive,
                    FileType = [fileType]
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    CursorState = null,
                    MaxRecords = 10,
                    IncludeMetadataHeader = true,
                    IncludeTransferHistory = false
                }
            };

            var queryBatchResponse = await senderOwnerClient.DriveRedux.QueryBatch(request);
            var theFileResponse = queryBatchResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theFileResponse);
            
            Assert.IsNull(theFileResponse.ServerMetadata.TransferHistory);
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task GetModifiedOnSenderCanExcludeRecipientTransferHistory()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            const string uploadedContent = "pie";
            const int fileType = 1033;
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = true,
                AppData = new()
                {
                    Content = uploadedContent,
                    FileType = fileType,
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
                Recipients = [recipientOwnerClient.Identity.OdinId],
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendAsync
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

            Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            Assert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            // Assert: file that was sent has peer transfer status updated
            var uploadedFileResponse1 = await senderOwnerClient.DriveRedux.GetFileHeader(uploadResult.File);
            Assert.IsTrue(uploadedFileResponse1.IsSuccessStatusCode);
            var uploadedFile1 = uploadedFileResponse1.Content;

            Assert.IsTrue(uploadedFile1.ServerMetadata.TransferHistory.Recipients.TryGetValue(recipientOwnerClient.Identity.OdinId, out var recipientStatus));
            Assert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            Assert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

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
                    MaxDate = UnixTimeUtc.Now().AddSeconds(-100).milliseconds,
                    IncludeTransferHistory = false
                }
            });

            Assert.IsTrue(queryModifiedResponse.IsSuccessStatusCode);
            var modifiedResults = queryModifiedResponse.Content;
            var fileInResults = modifiedResults.SearchResults.SingleOrDefault(r => r.FileId == uploadResult.File.FileId);
            Assert.IsNotNull(fileInResults);
            
            Assert.IsNull(fileInResults.ServerMetadata.TransferHistory);
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
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

            var circleId = Guid.NewGuid();
            var createCircleResponse = await recipientOwnerClient.Network.CreateCircle(circleId, "Circle with drive access", new PermissionSetGrantRequest()
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
            // Sender sends connection request
            //
            await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>());

            //
            // Recipient accepts; grants access to circle
            //
            await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, new List<GuidId>() { circleId });

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