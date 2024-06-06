using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests._Universal.ApiClient.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
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

        public static IEnumerable TestCases()
        {
            // yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
            // yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
            yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        }

        [Test]
        [TestCaseSource(nameof(TestCases))]
        public async Task RecipientCanSendReadReceipt(IApiClientContext callerContext,
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
                UseGlobalTransitId = true,
                Schedule = ScheduleOptions.SendAsync
            };

            var (uploadResponse, encryptedJsonContent64) = await UploadEncryptedMetadata(senderOwnerClient, targetDrive, transitOptions);

            Assert.IsTrue(uploadResponse.IsSuccessStatusCode);
            Assert.IsTrue(uploadResponse.StatusCode == HttpStatusCode.OK);
            var uploadResult = uploadResponse.Content;
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);
            Assert.IsTrue(uploadResult.RecipientStatus[recipientOwnerClient.Identity.OdinId] == TransferStatus.Enqueued);

            await callerContext.Initialize(senderOwnerClient);
            var driveClient = new UniversalDriveApiClient(senderOwnerClient.Identity.OdinId, callerContext.GetFactory());

            await driveClient.WaitForEmptyOutbox(targetDrive);

            // validate recipient got the file

            await recipientOwnerClient.DriveRedux.ProcessInbox(uploadResult.File.TargetDrive);

            var recipientFileResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
            Assert.IsTrue(recipientFileResponse.IsSuccessStatusCode);
            var recipientFile = recipientFileResponse.Content.SearchResults.SingleOrDefault();
            Assert.IsNotNull(recipientFile);
            Assert.IsTrue(recipientFile.FileMetadata.AppData.Content == encryptedJsonContent64);

            //
            // Send the read receipt
            //

            var sendReadReceiptResponse = await recipientOwnerClient.DriveRedux.SendReadReceipt([new ExternalFileIdentifier()
            {
                FileId = recipientFile.FileId,
                TargetDrive = recipientFile.TargetDrive
            }]);
            
            Assert.IsTrue(sendReadReceiptResponse.IsSuccessStatusCode);
            var sendReadReceiptResult = sendReadReceiptResponse.Content;
            Assert.IsNotNull(sendReadReceiptResult);

            Assert.IsTrue(sendReadReceiptResult.Results.TryGetValue(senderOwnerClient.Identity.OdinId, out var value));
            Assert.IsTrue(value == SendReadReceiptResultStatus.RequestAccepted);
            
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