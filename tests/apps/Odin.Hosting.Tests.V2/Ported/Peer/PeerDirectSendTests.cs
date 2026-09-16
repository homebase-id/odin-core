using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Query;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// Port of <c>_Universal/Peer/DirectSend/PeerDirectSendTests.cs</c>. Sends comment files to another
/// identity without storing them locally: the sender holds Read + WriteReactionsAndComments on the
/// recipient's drive and the comment is <i>direct written</i> — it lands on the recipient without the
/// recipient processing its inbox, which is why no <c>ProcessInboxAsync</c> call appears below.
/// </summary>
/// <remarks>
/// Checked port. Differences from the original, all inert:
/// <list type="bullet">
/// <item>The original's <c>PrepareScenario</c> created the shared drive on the recipient only;
/// <see cref="PeerFlow.CreatePeerDriveAsync"/> creates it on both sides. The sender never reads or
/// writes its own copy here — every assertion is against the recipient — so the extra drive is inert.</item>
/// <item>The original's trailing <c>DeleteScenario</c> (disconnect both identities) was cleanup only;
/// per-test reset covers it.</item>
/// <item>Every <c>WaitForEmptyOutbox(TransientTempDrive)</c> becomes <c>Sync.DrainOutboxAsync()</c> —
/// the V1 call is a passive poll on a background service the fast host never starts.</item>
/// </list>
/// Carried defect: <see cref="UniversalPeerDirectApiClient.DeleteFile"/> discards its
/// <c>ApiResponse</c>, so <c>CanDelete_Unencrypted_Comment</c> never checks that the delete request
/// was accepted — it only observes the resulting file state. Left as-is.
/// </remarks>
[TestFixture]
public class PeerDirectSendTests : V2Fixture
{
    private const DrivePermission CommentDrivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

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

        var sender = await LoginAsOwner(Identities.Frodo); //sender is the one who sends the comment
        var recipient = await LoginAsOwner(Identities.Sam);

        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = false;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = false;

        var recipientTargetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, CommentDrivePermissions,
            "comment target", allowAnonymousReads: false);

        var (standardFileUploadResult, _) =
            await UploadStandardFile(recipient, recipientTargetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGtidResponse = await recipient.V1.Drive.QueryByGlobalTransitId(
            standardFileUploadResult.GlobalTransitIdFileIdentifier);

        var recipientFileByGlobalTransitId = recipientFileByGtidResponse.Content?.SearchResults.SingleOrDefault();
        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(standardFileContent));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        // Sender replies with a comment
        var (commentTransitResult, _) = await TransferComment(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentTransitResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();
        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        //

        // File should be on recipient server and accessible by global transit id
        var qp = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
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

        var batchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var batch = batchResponse.Content;

        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        var receivedFile = batch.SearchResults.First();
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(commentFileContent));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId,
            Is.EqualTo(commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId));

        //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = true;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = true;

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, CommentDrivePermissions,
            "comment target", allowAnonymousReads: false);

        var (standardFileUploadResult, encryptedJsonContent64) =
            await UploadStandardFile(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitIdResponse =
            await recipient.V1.Drive.QueryByGlobalTransitId(standardFileUploadResult.GlobalTransitIdFileIdentifier);

        var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults?.SingleOrDefault();
        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentUploadResult, encryptedCommentJsonContent64) = await TransferComment(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentUploadResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();

        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        //

        // File should be on recipient server and accessible by global transit id
        var qp = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
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

        var batchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var batch = batchResponse.Content;
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        var receivedFile = batch.SearchResults.First();
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));

        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedCommentJsonContent64));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId,
            Is.EqualTo(commentUploadResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId));

        //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = true;

        const string commentFileContent = "Srsly!?? =O";
        const string updatedCommentFileContent = "Bruh! Srsly!?? =O";
        const bool commentIsEncrypted = true;

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, CommentDrivePermissions,
            "comment target", allowAnonymousReads: false);

        var (standardFileUploadResult, encryptedJsonContent64) =
            await UploadStandardFile(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitIdResponse =
            await recipient.V1.Drive.QueryByGlobalTransitId(standardFileUploadResult.GlobalTransitIdFileIdentifier);

        var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults.SingleOrDefault();
        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentTransitResult, encryptedCommentJsonContent64) = await TransferComment(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted,
            recipient);

        Assert.That(commentTransitResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued),
            $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

        await sender.Sync.DrainOutboxAsync();

        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        //

        // File should be on recipient server and accessible by global transit id
        var qp = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
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

        var batchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var batch = batchResponse.Content;
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        var receivedFile = batch.SearchResults.First();
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedCommentJsonContent64));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId,
            Is.EqualTo(commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId));

        // UnixTimeUtc is not IComparable — compare .milliseconds (see the V2 README).
        Assert.That(receivedFile.FileMetadata.TransitCreated.milliseconds, Is.GreaterThan(0));
        Assert.That(receivedFile.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(0));

        //Sender updates their comment

        var (_, encryptedUpdatedCommentJsonContent64) = await TransferComment(
            sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: updatedCommentFileContent,
            encrypted: commentIsEncrypted,
            recipient: recipient,
            overwriteFile: commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId,
            versionTag: receivedFile.FileMetadata.VersionTag);

        var updatedBatchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var updatedBatch = updatedBatchResponse.Content;
        Assert.That(updatedBatch.SearchResults.Count(), Is.EqualTo(1));
        var updatedReceivedFile = updatedBatch.SearchResults.First();
        Assert.That(updatedReceivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(updatedReceivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(updatedReceivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(updatedReceivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(updatedReceivedFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedUpdatedCommentJsonContent64));

        Assert.That(updatedReceivedFile.FileMetadata.TransitCreated.milliseconds, Is.GreaterThan(0));
        Assert.That(updatedReceivedFile.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(0));

        Assert.That(updatedReceivedFile.FileMetadata.GlobalTransitId,
            Is.EqualTo(commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId),
            "should still match original global transit id");
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = false;

        const string commentFileContent = "Srsly!?? =O";
        const string updatedCommentFileContent = "Bruh! Srsly!?? =O";
        const bool commentIsEncrypted = false;

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, CommentDrivePermissions,
            "comment target", allowAnonymousReads: false);

        var (standardFileUploadResult, _) = await UploadStandardFile(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitIdResponse =
            await recipient.V1.Drive.QueryByGlobalTransitId(standardFileUploadResult.GlobalTransitIdFileIdentifier);

        var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults?.SingleOrDefault();
        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(standardFileContent));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        // Sender replies with a comment
        var (commentTransitResult, _) = await TransferComment(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted,
            recipient: recipient);

        Assert.That(commentTransitResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();

        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        //

        // File should be on recipient server and accessible by global transit id
        var qp = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
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

        var batchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var batch = batchResponse.Content;
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        var receivedFile = batch.SearchResults.First();
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(commentFileContent));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId,
            Is.EqualTo(commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId));

        //Sender updates their comment

        var (_, _) = await TransferComment(
            sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: updatedCommentFileContent,
            encrypted: commentIsEncrypted,
            recipient: recipient,
            overwriteFile: commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId);

        var updatedBatchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var updatedBatch = updatedBatchResponse.Content;
        Assert.That(updatedBatch.SearchResults.Count(), Is.EqualTo(1));
        var updatedReceivedFile = updatedBatch.SearchResults.First();
        Assert.That(updatedReceivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(updatedReceivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(updatedReceivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(updatedReceivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(updatedReceivedFile.FileMetadata.AppData.Content, Is.EqualTo(updatedCommentFileContent));

        Assert.That(updatedReceivedFile.FileMetadata.GlobalTransitId,
            Is.EqualTo(commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId),
            "should still match original global transit id");
    }

    [Test]
    public async Task CanDelete_Unencrypted_Comment()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = false;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = false;

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, CommentDrivePermissions,
            "comment target", allowAnonymousReads: false);

        var (standardFileUploadResult, _) = await UploadStandardFile(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitIdResponse = await recipient.V1.Drive.QueryByGlobalTransitId(
            standardFileUploadResult.GlobalTransitIdFileIdentifier);

        var recipientFileByGlobalTransitId = recipientFileByGlobalTransitIdResponse.Content?.SearchResults?.SingleOrDefault();
        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(standardFileContent));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        // Sender replies with a comment
        var (commentTransitResult, _) = await TransferComment(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentTransitResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued),
            $"Should have been DeliveredToTargetDrive, actual status was {recipientStatus}");

        await sender.Sync.DrainOutboxAsync();

        //
        // Test results
        //

        // File should be on recipient server and accessible by global transit id
        var qp = new QueryBatchRequest
        {
            QueryParams = new FileQueryParamsV1()
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

        var batchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var batch = batchResponse.Content;
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        var receivedFile = batch.SearchResults.First();
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(commentFileContent));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId,
            Is.EqualTo(commentTransitResult.RemoteGlobalTransitIdFileIdentifier.GlobalTransitId));

        //
        //Delete the comment
        //

        await sender.V1.PeerDirect.DeleteFile(
            FileSystemType.Comment,
            commentTransitResult.RemoteGlobalTransitIdFileIdentifier,
            [recipient.Identity]);

        await sender.Sync.DrainOutboxAsync();
        //
        // See the comment is deleted
        //

        var softDeletedBatchResponse = await recipient.V1.Drive.QueryBatch(qp, FileSystemType.Comment);
        var softDeletedBatch = softDeletedBatchResponse.Content;
        Assert.That(softDeletedBatch.SearchResults.Count(), Is.EqualTo(1));
        var theDeletedFile = softDeletedBatch.SearchResults.SingleOrDefault();
        Assert.That(theDeletedFile, Is.Not.Null);
        Assert.That(theDeletedFile.FileState, Is.EqualTo(FileState.Deleted));
        Assert.That(theDeletedFile.FileSystemType, Is.EqualTo(FileSystemType.Comment));
    }

    /// <summary>
    /// Sends a standard file to a single recipient and performs basic assertions required by all tests
    /// </summary>
    private static async Task<(TransitResult, string encryptedJsonContent64)> TransferComment(
        OwnerSession sender,
        GlobalTransitIdFileIdentifier referencedFile,
        string uploadedContent,
        bool encrypted,
        OwnerSession recipient,
        Guid? overwriteFile = null,
        Guid? versionTag = null)
    {
        var fileMetadata = SampleMetadataData.CreateWithContent(default, uploadedContent, AccessControlList.Connected);
        fileMetadata.VersionTag = versionTag;
        fileMetadata.AllowDistribution = true;
        fileMetadata.IsEncrypted = encrypted;
        fileMetadata.ReferencedFile = referencedFile; //indicates the file about which this file is giving feed back

        var recipients = new List<string>() { recipient.Identity };

        ApiResponse<TransitResult> transitResultResponse;

        string encryptedJsonContent64 = null;
        if (encrypted)
        {
            (transitResultResponse, encryptedJsonContent64) = await sender.V1.PeerDirect.TransferEncryptedMetadata(
                remoteTargetDrive: referencedFile.TargetDrive,
                fileMetadata,
                recipients: recipients,
                overwriteGlobalTransitFileId: overwriteFile,
                fileSystemType: FileSystemType.Comment
            );
        }

        else
        {
            transitResultResponse = await sender.V1.PeerDirect.TransferMetadata(
                referencedFile.TargetDrive,
                fileMetadata,
                recipients: recipients,
                overwriteFile,
                fileSystemType: FileSystemType.Comment
            );
        }

        Assert.That(transitResultResponse.IsSuccessStatusCode, Is.True);
        var transitResult = transitResultResponse.Content;

        //
        // Basic tests first which apply to all calls
        //
        Assert.That(transitResult.RecipientStatus.Count, Is.EqualTo(1));

        await sender.Sync.DrainOutboxAsync();
        return (transitResult, encryptedJsonContent64);
    }

    private static async Task<(UploadResult, string encryptedJsonContent64)> UploadStandardFile(
        OwnerSession client, TargetDrive targetDrive, string uploadedContent, bool encrypted)
    {
        var fileMetadata = SampleMetadataData.CreateWithContent(200, uploadedContent, AccessControlList.Connected);
        fileMetadata.AllowDistribution = true;
        fileMetadata.IsEncrypted = encrypted;

        ApiResponse<UploadResult> uploadResponse;
        string encryptedJsonContent64 = null;
        if (encrypted)
        {
            (uploadResponse, encryptedJsonContent64) =
                await client.V1.Drive.UploadNewEncryptedMetadata(targetDrive, fileMetadata);
        }
        else
        {
            uploadResponse = await client.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata);
        }

        return (uploadResponse.Content, encryptedJsonContent64);
    }

    /// <summary>
    /// The peer-direct V1 client for a session. Built from the session's own identity+factory pair so
    /// one caller's identity can never be paired with another's factory (the reason
    /// <see cref="V1Handles"/> exists); peer-direct isn't on those handles yet.
    /// </summary>
}
