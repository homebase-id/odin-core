using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Util;
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

namespace Odin.Hosting.Tests._Universal.DriveTests.Inbox
{
    internal class FileSendResponse
    {
        public UploadResult UploadResult { get; set; }
        public string DecryptedContent { get; set; }
        public string EncryptedContent64 { get; set; }
    }

    public class PeerInboxSpeedTests
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


        [Test]
        public async Task TransferEncryptedContent_AndProcessInbox()
        {
            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);

            const DrivePermission drivePermissions = DrivePermission.Write;
            const int totalFileCount = 32;

            var targetDrive = TargetDrive.NewTargetDrive();
            await PrepareScenario(senderOwnerClient, recipientOwnerClient, targetDrive, drivePermissions);

            var fileSendResults = await SendFiles(senderOwnerClient, recipientOwnerClient, targetDrive, totalFileCount);
            Assert.IsTrue(fileSendResults.Count == totalFileCount);

            // await AssertFilesAreNotOnRecipientIdentity(recipientOwnerClient, fileSendResults);
            
            var ms = await Benchmark.MillisecondsAsync(async () =>
            {
                var processInboxResponse = await recipientOwnerClient.DriveRedux.ProcessInbox(targetDrive, batchSize: 100);
                Assert.IsTrue(processInboxResponse.IsSuccessStatusCode);
                Assert.IsTrue(processInboxResponse.Content.PoppedCount == 0);
                Assert.IsTrue(processInboxResponse.Content.TotalItems == 0);
            });

            var seconds = TimeSpan.FromMilliseconds(ms).TotalSeconds;
            Console.WriteLine($"Took {seconds} sec to process inbox with {fileSendResults.Count} files");
            
            // await AssertFilesAreOnRecipientIdentity(senderOwnerClient, recipientOwnerClient, fileSendResults);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        private async Task<List<FileSendResponse>> SendFiles(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
            TargetDrive targetDrive, int totalFiles)
        {
            var results = new List<FileSendResponse>();

            for (var i = 0; i < totalFiles; i++)
            {
                var fileContent = $"some string {i}";
                var (uploadResult, encryptedJsonContent64) = await SendStandardFile(senderOwnerClient, targetDrive, fileContent, recipientOwnerClient.Identity);

                Assert.IsTrue(uploadResult.RecipientStatus.TryGetValue(recipientOwnerClient.Identity.OdinId, out var recipientStatus));
                Assert.IsTrue(recipientStatus == TransferStatus.Enqueued, $"Should have been delivered, actual status was {recipientStatus}");

                results.Add(new FileSendResponse()
                {
                    UploadResult = uploadResult,
                    DecryptedContent = fileContent,
                    EncryptedContent64 = encryptedJsonContent64
                });
            }

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(targetDrive);
            return results;
        }

        private static async Task AssertFilesAreNotOnRecipientIdentity(OwnerApiClientRedux recipientOwnerClient, List<FileSendResponse> results)
        {
            foreach (var fileResponse in results)
            {
                //
                //  Assert recipient does not have the file when it is first sent
                //
                var qp = new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = fileResponse.UploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                        GlobalTransitId = new List<Guid>() { fileResponse.UploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        IncludeMetadataHeader = true,
                        MaxRecords = 10
                    }
                };

                var emptyBatchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp);
                var emptyBatch = emptyBatchResponse.Content;
                Assert.IsFalse(emptyBatch.SearchResults.Any(), "recipient should not have the file");
            }
        }

        private static async Task AssertFilesAreOnRecipientIdentity(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient,
            List<FileSendResponse> results)
        {
            foreach (var fileResponse in results)
            {
                //
                //  Assert recipient does not have the file when it is first sent
                //
                var qp = new QueryBatchRequest()
                {
                    QueryParams = new FileQueryParams()
                    {
                        TargetDrive = fileResponse.UploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                        GlobalTransitId = new List<Guid>() { fileResponse.UploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
                    },
                    ResultOptionsRequest = new QueryBatchResultOptionsRequest()
                    {
                        IncludeMetadataHeader = true,
                        MaxRecords = 10
                    }
                };

                // Now the File should be on recipient server and accessible by global transit id
                var queryBatchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp);
                var batch = queryBatchResponse.Content;
                Assert.IsTrue(batch.SearchResults.Count() == 1);
                var receivedFile = batch.SearchResults.First();
                Assert.IsTrue(receivedFile.FileState == FileState.Active);
                Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == senderOwnerClient.Identity.OdinId,
                    $"Sender should have been ${senderOwnerClient.Identity.OdinId}");
                Assert.IsTrue(receivedFile.FileMetadata.OriginalAuthor == senderOwnerClient.Identity.OdinId,
                    $"Original Author should have been ${senderOwnerClient.Identity.OdinId}");
                Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted);
                Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == fileResponse.EncryptedContent64);
                Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == fileResponse.UploadResult.GlobalTransitId);
            }
        }


        /// <summary>
        /// Sends a standard file to a single recipient and performs basic assertions required by all tests
        /// </summary>
        private async Task<(UploadResult, string encryptedJsonContent64)> SendStandardFile(OwnerApiClientRedux sender, TargetDrive targetDrive,
            string uploadedContent, TestIdentity recipient)
        {
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
                Recipients = [recipient.OdinId],
                RemoteTargetDrive = default
            };

            var (uploadResponse, encryptedJsonContent64) = await sender.DriveRedux.UploadNewEncryptedMetadata(
                fileMetadata,
                storageOptions,
                transitOptions
            );

            var uploadResult = uploadResponse.Content;

            //
            // Basic tests first which apply to all calls
            //
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);

            return (uploadResult, encryptedJsonContent64);
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
            await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, new List<GuidId>() { });

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