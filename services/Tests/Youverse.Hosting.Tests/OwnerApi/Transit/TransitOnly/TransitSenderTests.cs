using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.DriveCore.Query;
using Youverse.Core.Services.Drives.DriveCore.Storage;
using Youverse.Core.Services.Drives.FileSystem.Base.Upload;
using Youverse.Core.Services.Transit;
using Youverse.Core.Services.Transit.SendingHost;
using Youverse.Core.Storage;
using Youverse.Hosting.Tests.OwnerApi.ApiClient;

namespace Youverse.Hosting.Tests.OwnerApi.Transit.TransitOnly
{
    /// <summary>
    /// Tests to send comment files to another identity w/o storing them locally
    /// </summary>
    public class TransitSenderTests
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
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.JsonContent == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.PayloadIsEncrypted == standardFileIsEncrypted);

            // Sender replies with a comment
            var (commentTransitResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.DeliveredToTargetDrive,
                $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == commentFileContent);
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
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.JsonContent == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.PayloadIsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentUploadResult, encryptedCommentJsonContent64) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentUploadResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.DeliveredToTargetDrive,
                $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentUploadResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentUploadResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == encryptedCommentJsonContent64);
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

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

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
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.JsonContent == encryptedJsonContent64);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.PayloadIsEncrypted == standardFileIsEncrypted);

            //sender replies with a comment
            var (commentTransitResult, encryptedCommentJsonContent64) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted,
                recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.DeliveredToTargetDrive,
                $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == encryptedCommentJsonContent64);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);


            //Sender updates their comment

            var (updatedCommentTransitResult, encryptedUpdatedCommentJsonContent64) = await this.TransferComment(
                senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: updatedCommentFileContent,
                encrypted: commentIsEncrypted,
                recipient: recipient,
                overwriteFile: commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                versionTag: receivedFile.FileMetadata.VersionTag);

            var updatedBatch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(updatedBatch.SearchResults.Count() == 1);
            var updatedReceivedFile = updatedBatch.SearchResults.First();
            Assert.IsTrue(updatedReceivedFile.FileState == FileState.Active);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(updatedReceivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.AppData.JsonContent == encryptedUpdatedCommentJsonContent64);

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

            var senderOwnerClient = _scaffold.CreateOwnerApiClient(sender);
            var recipientOwnerClient = _scaffold.CreateOwnerApiClient(recipient);

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
            var recipientFileByGlobalTransitId = await recipientOwnerClient.Drive.QueryByGlobalTransitFileId(
                FileSystemType.Standard,
                standardFileUploadResult.GlobalTransitIdFileIdentifier);

            Assert.IsNotNull(recipientFileByGlobalTransitId);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.JsonContent == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.PayloadIsEncrypted == standardFileIsEncrypted);

            // Sender replies with a comment
            var (commentTransitResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted,
                recipient: recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.DeliveredToTargetDrive,
                $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

            //
            // Test results
            //

            //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
            // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);


            //Sender updates their comment

            var (updatedCommentTransitResult, _) = await this.TransferComment(
                senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: updatedCommentFileContent,
                encrypted: commentIsEncrypted,
                recipient: recipient,
                overwriteFile: commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            var updatedBatch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(updatedBatch.SearchResults.Count() == 1);
            var updatedReceivedFile = updatedBatch.SearchResults.First();
            Assert.IsTrue(updatedReceivedFile.FileState == FileState.Active);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(updatedReceivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(updatedReceivedFile.FileMetadata.AppData.JsonContent == updatedCommentFileContent);

            Assert.IsTrue(updatedReceivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
                "should still match original global transit id");

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        [Test]
        public async Task CanDelete_Unencrypted_Comment()
        {
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
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.AppData.JsonContent == standardFileContent);
            Assert.IsTrue(recipientFileByGlobalTransitId.FileMetadata.PayloadIsEncrypted == standardFileIsEncrypted);

            // Sender replies with a comment
            var (commentTransitResult, _) = await this.TransferComment(senderOwnerClient,
                standardFileUploadResult.GlobalTransitIdFileIdentifier,
                uploadedContent: commentFileContent,
                encrypted: commentIsEncrypted, recipient);

            Assert.IsTrue(commentTransitResult.RecipientStatus.TryGetValue(recipient.OdinId, out var recipientStatus));
            Assert.IsTrue(recipientStatus == TransferStatus.DeliveredToTargetDrive,
                $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

            //
            // Test results
            //

            // File should be on recipient server and accessible by global transit id
            var qp = new FileQueryParams()
            {
                TargetDrive = commentTransitResult.RemoteGlobalTransitIdFileIdentifier.TargetDrive,
                GlobalTransitId = new List<Guid>() { commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId }
            };

            var batch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(batch.SearchResults.Count() == 1);
            var receivedFile = batch.SearchResults.First();
            Assert.IsTrue(receivedFile.FileState == FileState.Active);
            Assert.IsTrue(receivedFile.FileMetadata.SenderOdinId == sender.OdinId, $"Sender should have been ${sender.OdinId}");
            Assert.IsTrue(receivedFile.FileMetadata.PayloadIsEncrypted == commentIsEncrypted);
            Assert.IsTrue(receivedFile.FileMetadata.AppData.JsonContent == commentFileContent);
            Assert.IsTrue(receivedFile.FileMetadata.GlobalTransitId == commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

            //
            //Delete the comment
            //

            await senderOwnerClient.Transit.DeleteFile(
                FileSystemType.Comment,
                commentTransitResult.RemoteGlobalTransitIdFileIdentifier,
                new List<string>() { recipient.OdinId });

            //
            // See the comment is deleted
            //

            var softDeletedBatch = await recipientOwnerClient.Drive.QueryBatch(FileSystemType.Comment, qp);
            Assert.IsTrue(softDeletedBatch.SearchResults.Count() == 1);
            var theDeletedFile = softDeletedBatch.SearchResults.SingleOrDefault();
            Assert.IsNotNull(theDeletedFile);
            Assert.IsTrue(theDeletedFile.FileState == FileState.Deleted);
            Assert.IsTrue(theDeletedFile.FileSystemType == FileSystemType.Comment);

            await this.DeleteScenario(senderOwnerClient, recipientOwnerClient);
        }

        //

        /// <summary>
        /// Sends a standard file to a single recipient and performs basic assertions required by all tests
        /// </summary>
        private async Task<(TransitResult, string encryptedJsonContent64)> TransferComment(
            OwnerApiClient sender,
            GlobalTransitIdFileIdentifier referencedFile,
            string uploadedContent,
            bool encrypted,
            TestIdentity recipient,
            Guid? overwriteFile = null,
            Guid? versionTag = null)
        {
            var fileMetadata = new UploadFileMetadata()
            {
                VersionTag = versionTag,
                AllowDistribution = true,
                ContentType = "application/json",
                PayloadIsEncrypted = encrypted,

                //indicates the file about which this file is giving feed back
                ReferencedFile = referencedFile,

                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = uploadedContent,
                    FileType = default,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.OwnerOnly
            };

            var recipients = new List<string>() { recipient.OdinId };

            TransitResult transitResult;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (transitResult, encryptedJsonContent64) = await sender.Transit.TransferEncryptedFile(
                    FileSystemType.Comment,
                    fileMetadata,
                    recipients: recipients,
                    remoteTargetDrive: referencedFile.TargetDrive,
                    payloadData: "",
                    overwriteGlobalTransitFileId: overwriteFile,
                    thumbnail: null
                );
            }
            else
            {
                transitResult = await sender.Transit.TransferFile(
                    FileSystemType.Comment,
                    fileMetadata,
                    recipients: recipients,
                    remoteTargetDrive: referencedFile.TargetDrive,
                    payloadData: "",
                    overwriteGlobalTransitFileId: overwriteFile,
                    thumbnail: null
                );
            }

            //
            // Basic tests first which apply to all calls
            //
            Assert.IsTrue(transitResult.RecipientStatus.Count == 1);

            return (transitResult, encryptedJsonContent64);
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
            // Recipient creates a circle with target drive, read and write access
            //
            var expectedPermissionedDrive = new PermissionedDrive()
            {
                Drive = recipientTargetDrive.TargetDriveInfo,
                Permission = drivePermissions
            };

            var recipientCircle = await recipientOwnerClient.Network.CreateCircle("Circle with drive access", new PermissionSetGrantRequest()
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
            await senderOwnerClient.Network.SendConnectionRequest(recipientOwnerClient.Identity, new List<GuidId>() { });

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
                ContentType = "application/json",
                PayloadIsEncrypted = encrypted,
                AppData = new()
                {
                    ContentIsComplete = true,
                    JsonContent = uploadedContent,
                    FileType = 200,
                    GroupId = default,
                    Tags = default
                },
                AccessControlList = AccessControlList.Connected
            };

            UploadResult uploadResult;
            string encryptedJsonContent64 = null;
            if (encrypted)
            {
                (uploadResult, encryptedJsonContent64) = await client.Drive.UploadEncryptedFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
            }
            else
            {
                uploadResult = await client.Drive.UploadFile(FileSystemType.Standard, targetDrive, fileMetadata, "");
            }

            return (uploadResult, encryptedJsonContent64);
        }

        private async Task DeleteScenario(OwnerApiClient senderOwnerClient, OwnerApiClient recipientOwnerClient)
        {
            await _scaffold.OldOwnerApi.DisconnectIdentities(senderOwnerClient.Identity.OdinId, recipientOwnerClient.Identity.OdinId);
        }
    }
}