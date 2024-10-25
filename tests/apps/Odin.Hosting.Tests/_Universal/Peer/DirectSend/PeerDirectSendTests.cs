using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Owner;
using Odin.Hosting.Tests._Universal.DriveTests;
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

namespace Odin.Hosting.Tests._Universal.Peer.DirectSend
{
    /// <summary>
    /// Tests to send comment files to another identity w/o storing them locally
    /// </summary>
    public class PeerDirectSendTests
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
        public async Task CanTransfer_Unencrypted_Comment()
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

            var sender = TestIdentities.Frodo; //sender is the one who sends the comment
            var recipient = TestIdentities.Samwise;

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = false;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = false;

            var recipientTargetDrive = await PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, _) =
                await UploadStandardFile(recipientOwnerClient, recipientTargetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGtidResponse = await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            var recipientFileByGlobalTransitId = recipientFileByGtidResponse.Content?.SearchResults.SingleOrDefault();
            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            // Sender replies with a comment
            var (commentTransitResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                    GlobalTransitId = new List<Guid>()
                    {
                        commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId
                    }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true,
                    IncludeTransferHistory = false
                }
            };

            var batchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var batch = batchResponse.Content;

            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original Author should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

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

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

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
            var recipientFileByGlobalTransitIdResponse =
                await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(standardFileUploadResult.GlobalTransitIdFileIdentifier);

            var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults?.SingleOrDefault();
            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, encryptedCommentJsonContent64) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = commentUploadResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                    GlobalTransitId = new List<Guid>() { commentUploadResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true,
                }
            };

            var batchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var batch = batchResponse.Content;
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original Author should have been ${sender.OdinId}");

            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == encryptedCommentJsonContent64);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentUploadResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }


        [Test]
        public async Task CanTransfer_AndUpdate_Encrypted_Comment_S2110()
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

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = true;

            const string commentFileContent = "Srsly!?? =O";
            const string updatedCommentFileContent = "Bruh! Srsly!?? =O";
            const bool commentIsEncrypted = true;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, encryptedJsonContent64) =
                await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitIdResponse =
                await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(standardFileUploadResult.GlobalTransitIdFileIdentifier);

            var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults.SingleOrDefault();
            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentTransitResult, encryptedCommentJsonContent64) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted,
                recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued,
                $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                    GlobalTransitId = new List<Guid>() { commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            };

            var batchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var batch = batchResponse.Content;
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original Author should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == encryptedCommentJsonContent64);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            Assert.IsTrue(receivedFile.FileMetadata.TransitCreated > 0);
            Assert.IsTrue(receivedFile.FileMetadata.TransitUpdated == 0);


            //Sender updates their comment

            var (_, encryptedUpdatedCommentJsonContent64) = await this.TransferComment(
                senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: updatedCommentFileContent,
                encrypted: commentIsEncrypted,
                recipient: recipient,
                overwriteFile: commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                versionTag: receivedFile.FileMetadata.VersionTag);


            var updatedBatchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var updatedBatch = updatedBatchResponse.Content;
            Assert.IsTrue(updatedBatch.SearchResults.Count() == 1);
            var updatedReceivedFile = updatedBatch.SearchResults.First();
            Assert.IsTrue(updatedReceivedFile.FileState == FileState.Active);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(updatedReceivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original Author should have been ${sender.OdinId}");
            Assert.IsTrue(updatedReceivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.AppData.Content == encryptedUpdatedCommentJsonContent64);

            Assert.IsTrue(updatedReceivedFile.FileMetadata.TransitCreated > 0);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.TransitUpdated == 0);

            Assert.IsTrue(updatedReceivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                "should still match original global transit id");


            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task CanTransfer_AndUpdate_Unencrypted_Comment()
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

            var senderOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = false;

            const string commentFileContent = "Srsly!?? =O";
            const string updatedCommentFileContent = "Bruh! Srsly!?? =O";
            const bool commentIsEncrypted = false;

            var targetDrive = await this.PrepareScenario(senderOwnerClient, recipientOwnerClient, drivePermissions);

            var (standardFileUploadResult, _) = await UploadStandardFile(recipientOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitIdResponse =
                await recipientOwnerClient.DriveRedux.QueryByGlobalTransitId(standardFileUploadResult.GlobalTransitIdFileIdentifier);

            var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults?.SingleOrDefault();
            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            // Sender replies with a comment
            var (commentTransitResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted,
                recipient: recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued);

            await senderOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                    GlobalTransitId = new List<Guid>() { commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            };

            var batchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var batch = batchResponse.Content;
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);


            //Sender updates their comment

            var (_, _) = await this.TransferComment(
                senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: updatedCommentFileContent,
                encrypted: commentIsEncrypted,
                recipient: recipient,
                overwriteFile: commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            var updatedBatchResponse = await recipientOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var updatedBatch = updatedBatchResponse.Content;
            Assert.IsTrue(updatedBatch.SearchResults.Count() == 1);
            var updatedReceivedFile = updatedBatch.SearchResults.First();
            Assert.IsTrue(updatedReceivedFile.FileState == FileState.Active);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(updatedReceivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original Author should have been ${sender.OdinId}");
            Assert.IsTrue(updatedReceivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.AppData.Content == updatedCommentFileContent);

            Assert.IsTrue(updatedReceivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                "should still match original global transit id");

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task CanDelete_Unencrypted_Comment()
        {
            var sender = TestIdentities.Frodo;
            var recipient = TestIdentities.Samwise;

            var frodoOwnerClient = _scaffold.CreateOwnerApiClientRedux(sender);
            var samwiseOwnerClient = _scaffold.CreateOwnerApiClientRedux(recipient);

            const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
            const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
            const bool standardFileIsEncrypted = false;

            const string commentFileContent = "Srsly!?? =O";
            const bool commentIsEncrypted = false;

            var targetDrive = await this.PrepareScenario(frodoOwnerClient, samwiseOwnerClient, drivePermissions);

            var (standardFileUploadResult, _) = await UploadStandardFile(samwiseOwnerClient, targetDrive, standardFileContent, standardFileIsEncrypted);

            //
            // Assert that the recipient server has the file by global transit id
            //
            var recipientFileByGlobalTransitIdResponse = await samwiseOwnerClient.DriveRedux.QueryByGlobalTransitId(
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults?.SingleOrDefault();
            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.Content == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted == standardFileIsEncrypted);

            // Sender replies with a comment
            var (commentTransitResult, _) = await this.TransferComment(frodoOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.Enqueued,
                $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");


            await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);

            //
            // Test results
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new QueryBatchRequest
            {
                QueryParams = new FileQueryParams()
                {
                    TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                    GlobalTransitId = new List<Guid>() { commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
                },
                ResultOptionsRequest = new QueryBatchResultOptionsRequest
                {
                    MaxRecords = 10,
                    IncludeMetadataHeader = true
                }
            };

            var batchResponse = await samwiseOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var batch = batchResponse.Content;
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.OriginalAuthor == sender.OdinId, $"Original Author should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.IsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.Content == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            //
            //Delete the comment
            //

            await frodoOwnerClient.PeerDirect.DeleteFile(
                FileSystemType.Comment,
                commentTransitResult.RemoteGlobalTransitIdFileIdentifier,
                [recipient.OdinId]);

            await frodoOwnerClient.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
            //
            // See the comment is deleted
            //

            var softDeletedBatchResponse = await samwiseOwnerClient.DriveRedux.QueryBatch(qp, FileSystemType.Comment);
            var softDeletedBatch = softDeletedBatchResponse.Content;
            Assert.IsTrue(softDeletedBatch.SearchResults.Count() == 1);
            var theDeletedFile = softDeletedBatch.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theDeletedFile);
            Assert.IsTrue(theDeletedFile.FileState == FileState.Deleted);
            Assert.IsTrue(theDeletedFile.FileSystemType == FileSystemType.Comment);

            await this.DeleteScenario(frodoOwnerClient, samwiseOwnerClient);
        }


        /// <summary>
        /// Sends a standard file to a single recipient and performs basic assertions required by all tests
        /// </summary>
        private async Task<(TransitResult, string encryptedJsonContent64)> TransferComment(
            OwnerApiClientRedux sender,
            GlobalTransitIdFileIdentifier referencedFile,
            string uploadedContent,
            bool encrypted,
            TestIdentity recipient,
            Guid? overwriteFile = null,
            Guid? versionTag = null)
        {
            var fileMetadata = SampleMetadataData.CreateWithContent(default, uploadedContent, AccessControlList.Connected);
            fileMetadata.VersionTag = versionTag;
            fileMetadata.AllowDistribution = true;
            fileMetadata.IsEncrypted = encrypted;
            fileMetadata.ReferencedFile = referencedFile; //indicates the file about which this file is giving feed back

            var recipients = new List<string>() { recipient.OdinId };

            ApiResponse<TransitResult> transitResultResponse;

            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (transitResultResponse, encryptedJsonContent64) = await sender.PeerDirect.TransferEncryptedMetadata(
                    remoteTargetDrive: referencedFile.TargetDrive,
                    fileMetadata,
                    recipients: recipients,
                    overwriteGlobalTransitFileId: overwriteFile,
                    fileSystemType: FileSystemType.Comment
                );
            }

            else
            {
                transitResultResponse = await sender.PeerDirect.TransferMetadata(
                    referencedFile.TargetDrive,
                    fileMetadata,
                    recipients: recipients,
                    overwriteFile,
                    fileSystemType: FileSystemType.Comment
                );
            }

            Assert.IsTrue(transitResultResponse.IsSuccessStatusCode);
            var transitResult = transitResultResponse.Content;

            //
            // Basic tests first which apply to all calls
            //
            Assert.IsTrue(transitResult.RecipientStatus.Count == 1);


            await sender.DriveRedux.WaitForEmptyOutbox(SystemDriveConstants.TransientTempDrive);
            return (transitResult, encryptedJsonContent64);
        }

        private async Task<TargetDrive> PrepareScenario(
            OwnerApiClientRedux senderOwnerClient,
            OwnerApiClientRedux recipientOwnerClient,
            DrivePermission drivePermissions)
        {
            var targetDrive = TargetDrive.NewTargetDrive();

            //
            // Recipient creates a target drive
            //
            await recipientOwnerClient.DriveManager.CreateDrive(
                targetDrive: targetDrive,
                name: "Target drive on recipient",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            //
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = targetDrive,
                Permission = drivePermissions
            };

            var recipientCircleId = Guid.NewGuid();
            await recipientOwnerClient.Network.CreateCircle(recipientCircleId, "Circle with drive access", new PermissionSetGrantRequest()
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
            await senderOwnerClient.Connections.SendConnectionRequest(recipientOwnerClient.Identity.OdinId, []);

            //
            // Recipient accepts; grants access to circle
            //
            await recipientOwnerClient.Connections.AcceptConnectionRequest(senderOwnerClient.Identity.OdinId, [recipientCircleId]);

            return targetDrive;
        }

        private async Task<(UploadResult, string encryptedJsonContent64)> UploadStandardFile(OwnerApiClientRedux client, TargetDrive targetDrive,
            string uploadedContent, bool encrypted)
        {
            var fileMetadata = SampleMetadataData.CreateWithContent(200, uploadedContent, AccessControlList.Connected);
            fileMetadata.AllowDistribution = true;
            fileMetadata.IsEncrypted = encrypted;

            ApiResponse<UploadResult> uploadResponse;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (uploadResponse, encryptedJsonContent64) =
                    await client.DriveRedux.UploadNewEncryptedMetadata(targetDrive, fileMetadata);
            }
            else
            {
                uploadResponse = await client.DriveRedux.UploadNewMetadata(targetDrive, fileMetadata);
            }

            return (uploadResponse.Content, encryptedJsonContent64);
        }

        private async Task DeleteScenario(OwnerApiClientRedux senderOwnerClient, OwnerApiClientRedux recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}