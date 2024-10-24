using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Core.Storage;
using Odin.Hosting.Tests.OwnerApi.ApiClient;
using Refit;

namespace Odin.Hosting.Tests.OwnerApi.Transit.Routing
{
    /// https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/transit/transit_routing.md
    public class TransitCommentFileRoutingTests
    {
        private WebScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
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
        public async Task CanTransfer_Unencrypted_Comment_S2110()
        {
            /*
             Success Test - Comment
                Valid ReferencedFile (global transit id)
                Sender has storage Key
                Sender has write access
                Upload standard file - encrypted = false
                Upload comment file - encrypted = false
                Should succeed (S2110)
                    Direct write comment
                    Comment is not distributed
                    ReferencedFile summary updated
                    ReferencedFile is distributed to followers
             */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = false;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = false;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, _) = await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued, $"Should have been delivered, actual status was {recipientStatus}");

            await senderOwnerClient.Transit.WaitForEmptyOutbox(targetDrive);

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentUploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentUploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original author should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentUploadResult.GlobalTransitId);

            //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task CanTransfer_Encrypted_Comment_S2110()
        {
            /*
             Success Test - Comment
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender has write access
                Sender has storage Key (read access)
                Valid ReferencedFile (global transit id)
                Should succeed (S2110)
                    Direct write comment
                    Comment is not distributed
                    ReferencedFile summary updated
                    ReferencedFile is distributed to followers
             */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = true;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = true;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, encryptedJsonContent64) =
                await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, encryptedCommentJsonContent64) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued, $"Should have been delivered, actual status was {recipientStatus}");

            await senderOwnerClient.Transit.WaitForEmptyOutbox(targetDrive);

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentUploadResult.GlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentUploadResult.GlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original Author should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == encryptedCommentJsonContent64);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentUploadResult.GlobalTransitId);

            //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailsWhenSenderCannotWriteCommentOnRecipientServer()
        {
            /*
             Failure Test - Comment
                Fails when sender cannot write to target drive on recipients server
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender does not have write access (S2000)
                Sender has storage Key (read access)
                Valid ReferencedFile (global transit id)
                Should fail
                throws 403 - S2010
             */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = true;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = true;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, encryptedJsonContent64) =
                await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, encryptedCommentJsonContent64) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued,
                $"Should have been RecipientReturnedAccessDenied, actual status was {recipientStatus}");

            //
            // Validate the transfer history was updated correctly
            //
            await senderOwnerClient.DriveRedux.WaitForTransferStatus(commentUploadResult.File,
                recipientOwnerClient.Identity.OdinId,
                LatestTransferStatus.RecipientIdentityReturnedAccessDenied,
                FileSystemType.Comment);
            
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailsWhenSenderSpecifiesInvalidReferencedFile_S2030()
        {
            /*
             Fails when sender provides invalid  global transit id
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender has write access (S2000)
                Sender has storage Key (read access)
                Invalid ReferencedFile (global transit id)
                Should fail
                throws Bad Request - S2030
             */
            
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = true;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = true;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, encryptedJsonContent64) =
                await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            var invalidReferencedFile = new GlobalTransitIdFileIdentifier()
            {
                GlobalTransitId = Guid.NewGuid(),
                TargetDrive = targetDrive
            };

            //sender replies with a comment
            var (commentUploadResult, _) = await this.TransferComment(senderOwnerClient,
                invalidReferencedFile,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued,
                $"Should have been delivered, actual status was {recipientStatus}");

            //
            // Validate the transfer history was updated correctly
            //
            await senderOwnerClient.DriveRedux.WaitForTransferStatus(commentUploadResult.File,
                recipientOwnerClient.Identity.OdinId,
                LatestTransferStatus.RecipientIdentityReturnedBadRequest,
                FileSystemType.Comment);
            
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test1()
        {
            /*
             Fails when encryption do not match between from a comment to its ReferencedFile
                Test 1
                Upload standard file - encrypted = true
                Upload comment file - encrypted = false
                Sender has write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                Bad Request (S2100)
             */

            _scaffold.SetAssertLogEventsAction(logEvents =>
            {
                var errorLogs = logEvents[Serilog.Events.LogEventLevel.Error];
                Assert.That(errorLogs.Count, Is.EqualTo(1), "Unexpected number of Error log events");
                Assert.That(errorLogs[0].Exception!.Message,
                    Is.EqualTo("Remote identity host failed: Referenced filed and metadata payload encryption do not match"));
            });

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = true;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = false;


            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, encryptedJsonContent64) =
                await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued,
                $"Should have been delivered, actual status was {recipientStatus}");
            
            //
            // Validate the transfer history was updated correctly
            //
            await senderOwnerClient.DriveRedux.WaitForTransferStatus(commentUploadResult.File,
                recipientOwnerClient.Identity.OdinId,
                LatestTransferStatus.RecipientIdentityReturnedServerError,
                FileSystemType.Comment);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test2()
        {
            /*
              Fails when encryption do not match between from a comment to its ReferencedFile

                Test 2
                Upload standard file - encrypted = false
                Upload comment file - encrypted = true
                Sender does has write access (S2000)
                Sender has storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                Bad Request (S2100)
             */

            _scaffold.SetAssertLogEventsAction(logEvents =>
            {
                var errorLogs = logEvents[Serilog.Events.LogEventLevel.Error];
                Assert.That(errorLogs.Count, Is.EqualTo(1), "Unexpected number of Error log events");
                Assert.That(errorLogs[0].Exception!.Message,
                    Is.EqualTo("Remote identity host failed: Referenced filed and metadata payload encryption do not match"));
            });

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = false;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = true;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, encryptedJsonContent64) =
                await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, encryptedCommentJsonContent64) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued,
                $"Should have been delivered, actual status was {recipientStatus}");

            //
            // Validate the transfer history was updated correctly
            //
            await senderOwnerClient.DriveRedux.WaitForTransferStatus(commentUploadResult.File,
                recipientOwnerClient.Identity.OdinId,
                LatestTransferStatus.RecipientIdentityReturnedServerError,
                FileSystemType.Comment);
            
            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task FailsWhenCommentFileIsEncryptedAndSenderHasNoDriveStorageKeyOnRecipientServer_S2210()
        {
            /*
             Fails when file is encrypted and there is no drive storage key
                Comment:
                Test 1
                Upload standard file - encrypted = true
                Upload comment file - encrypted = true
                Sender has write access
                Sender does not have storage Key
                Valid ReferencedFile (global transit id)
                Should fail
                403
             */

            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

            const DrivePermission drivePermissions = DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = true;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = true;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, encryptedJsonContent64) =
                await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, _) = await TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var transferStatus));
            Assert.IsTrue(transferStatus == TransferStatus.Enqueued, $"Should have been delivered, actual status was {transferStatus}");

            //
            // Validate the transfer history was updated correctly
            //
            await senderOwnerClient.DriveRedux.WaitForTransferStatus(commentUploadResult.File,
                recipientOwnerClient.Identity.OdinId,
                LatestTransferStatus.RecipientIdentityReturnedAccessDenied,
                FileSystemType.Comment);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        //

        /// <summary>
        /// Sends a standard file to a single recipient and performs basic assertions required by all tests
        /// </summary>
        private async Task<(UploadResult, string encryptedJsonContent64)> TransferComment(
            OwnerApiClient sender,
            GlobalTransitIdFileIdentifier referencedFile,
            string uploadedContent,
            bool encrypted,
            TestIdentity recipient)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = encrypted,

                //indicates the file about which this file is giving feed back
                ReferencedFile = referencedFile,

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
                Drive = referencedFile.TargetDrive
            };

            var transitOptions = new TransitOptions()
            {
                Recipients = new List<string>() { recipient.OdinId },
                RemoteTargetDrive = default,
            };

            ApiResponse<UploadResult> uploadResponse;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (uploadResponse, encryptedJsonContent64) = await
                    sender.DriveRedux.UploadNewEncryptedMetadata(fileMetadata, storageOptions, transitOptions, FileSystemType.Comment);
            }
            else
            {
                uploadResponse = await sender.DriveRedux.UploadNewMetadata(
                    fileMetadata,
                    storageOptions,
                    transitOptions,
                    FileSystemType.Comment
                );
            }

            var uploadResult = uploadResponse.Content;

            //
            // Basic tests first which apply to all calls
            //
            Assert.IsTrue(uploadResult.RecipientStatus.Count == 1);

            return (uploadResult, encryptedJsonContent64);
        }


        private async Task<TargetDrive> PrepareScenario(
            OwnerApiClient senderOwnerClient,
            OwnerApiClient recipientOwnerClient,
            DrivePermission drivePermissions)
        {
            //
            // Recipient creates a target drive
            //
            var recipientTargetDrive = await recipientOwnerClient.Drive.CreateDrive(
                targetDrive: TargetDrive.NewTargetDrive(),
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            //
            // Sender needs this same drive in order to send across files
            //
            var senderTargetDrive = await senderOwnerClient.Drive.CreateDrive(
                targetDrive: recipientTargetDrive.TargetDriveInfo,
                name: "Target drive on sender",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);


            //
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = recipientTargetDrive.TargetDriveInfo,
                Permission = drivePermissions
            };

            var recipientCircle = await recipientOwnerClient.Membership.CreateCircle("Circle with drive access", new PermissionSetGrantRequest()
            {
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = expectedPermissionedDrive
                    }
                }
            });

            //
            // Sender sends connection request
            //
            await senderOwnerClient.Network.SendConnectionRequestTo(recipientOwnerClient.Identity, new List<GuidId>() { });

            //
            // Recipient accepts; grants access to circle
            //
            await recipientOwnerClient.Network.AcceptConnectionRequest(senderOwnerClient.Identity, new List<GuidId>() { recipientCircle.Id });

            // 
            // Test: At this point: recipient should have an ICR record on sender's identity that does not have a key
            // 

            var senderConnectionInfo = await recipientOwnerClient.Network.GetConnectionInfo(senderOwnerClient.Identity);

            Assert.IsNotNull(senderConnectionInfo.AccessGrant.CircleGrants.SingleOrDefault(cg =>
                cg.DriveGrants.Any(dg => dg.PermissionedDrive == recipientCircle.DriveGrants.Single().PermissionedDrive)));

            return recipientTargetDrive.TargetDriveInfo;
        }

        private async Task<(UploadResult, string encryptedJsonContent64)> UploadStandardFile(OwnerApiClient client, TargetDrive targetDrive,
            string uploadedContent, bool encrypted)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                AllowDistribution = true,
                IsEncrypted = encrypted,
                AppData = new()
                {
                    Content = uploadedContent,
                    FileType = 200,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.Connected
            };

            ApiResponse<UploadResult> uploadResponse;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (uploadResponse, encryptedJsonContent64) =
                    await client.DriveRedux.UploadNewEncryptedMetadata(targetDrive, fileMetadata);
                // (uploadResult, encryptedJsonContent64, _) = await client.Drive.UploadEncryptedFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
            }
            else
            {
                uploadResponse = await client.DriveRedux.UploadNewMetadata(targetDrive, fileMetadata);
                // uploadResult = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
            }

            return (uploadResponse.Content, encryptedJsonContent64);
        }

        private async Task DeleteScenario(OwnerApiClient senderOwnerClient, OwnerApiClient recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}