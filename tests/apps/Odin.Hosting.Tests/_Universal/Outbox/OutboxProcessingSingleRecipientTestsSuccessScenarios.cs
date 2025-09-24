using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
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
using Refit;

namespace Odin.Hosting.Tests._Universal.Outbox
{
    public class OutboxProcessingSingleRecipientTestsSuccessScenarios
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
                {"Job__EnableJobBackgroundService", "true"},
                {"Job__Enabled", "true"},
            };
        
            _scaffold.RunBeforeAnyTests(envOverrides: env, testIdentities: new List<TestIdentity>() { TestIdentities.Frodo, TestIdentities.Samwise });
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

            var transitOptions = new TransitOptions()
            {
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResponse, encryptedJsonContent64) = await UploadEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

            ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            ClassicAssert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            await driveClient.WaitForEmptyOutbox(targetDrive);

            if (expectedStatusCode == HttpStatusCode.OK)
            {
                // validate recipient got the file

                await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult.File.TargetDrive);

                var recipientFileResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
                ClassicAssert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
                var recipientFile = recipientFileResponse.Content.SearchResults.SingleOrDefault();
                ClassicAssert.IsNotNull(recipientFile);
                ClassicAssert.IsTrue(recipientFile.FileMetadata.AppData.Content == encryptedJsonContent64);

                //
                // Validate the transfer history was updated correctly
                //
                var getHistoryResponse = await senderOwnerClient.DriveRedux.GetTransferHistory(uploadResult.File);
                ClassicAssert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
                var theHistory = getHistoryResponse.Content;
                ClassicAssert.IsNotNull(theHistory);
                var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);
                
                ClassicAssert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
                ClassicAssert.IsFalse(recipientStatus.IsInOutbox);
                ClassicAssert.IsFalse(recipientStatus.IsReadByRecipient);
                ClassicAssert.IsTrue(recipientStatus.LatestTransferStatus == LatestTransferStatus.Delivered);
                ClassicAssert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);
            }

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
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

            ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            ClassicAssert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            // Assert: file that was sent has peer transfer status updated
            var getHistoryResponse = await senderOwnerClient.DriveRedux.GetTransferHistory(uploadResult.File);
            ClassicAssert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
            var theHistory = getHistoryResponse.Content;
            ClassicAssert.IsNotNull(theHistory);
            var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);
            ClassicAssert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            ClassicAssert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

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
            ClassicAssert.IsNotNull(theFileResponse);

            ClassicAssert.IsNull(theFileResponse.ServerMetadata.TransferHistory);
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
                Recipients = [recipientOwnerClient.Identity.OdinId]
            };

            var (uploadResponse, _) = await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);

            ClassicAssert.IsTrue(uploadResponse.IsSuccessStatusCode);
            ClassicAssert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            ClassicAssert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            ClassicAssert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            // Assert: file that was sent has peer transfer status updated
            var getHistoryResponse = await senderOwnerClient.DriveRedux.GetTransferHistory(uploadResult.File);
            ClassicAssert.IsTrue(getHistoryResponse.IsSuccessStatusCode);
            var theHistory = getHistoryResponse.Content;
            ClassicAssert.IsNotNull(theHistory);
            var recipientStatus = theHistory.GetHistoryItem(recipientOwnerClient.Identity.OdinId);
            ClassicAssert.IsNotNull(recipientStatus, "There should be a status update for the recipient");
            ClassicAssert.IsTrue(recipientStatus.LatestSuccessfullyDeliveredVersionTag == uploadResult.NewVersionTag);

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
                    MaxDate = UnixTimeUtc.Now().AddSeconds(+100).milliseconds,
                    IncludeTransferHistory = false
                }
            });

            ClassicAssert.IsTrue(queryModifiedResponse.IsSuccessStatusCode);
            var modifiedResults = queryModifiedResponse.Content;
            var fileInResults = modifiedResults.SearchResults.SingleOrDefault(r => r.FileId == uploadResult.File.FileId);
            ClassicAssert.IsNotNull(fileInResults);

            ClassicAssert.IsNull(fileInResults.ServerMetadata.TransferHistory);
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test, Ignore("How do i test this?  see notes in test")]
        public Task CanSetDependencyIdOnOutboxItem()
        {
            //TODO: how do i test this?  It seems it will require me to inject debug-test code to the outbox
            //processor to slow down processing enough to test the dependency ordering of files received
            return Task.CompletedTask;
        }

        private async Task<(ApiResponse<UploadResult> response, string encryptedJsonContent64)> UploadEncryptedMetadata(OwnerApiClientRedux senderOwnerClient,
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


            return await senderOwnerClient.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );
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

            ClassicAssert.IsTrue(createCircleResponse.IsSuccessStatusCode);

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