using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests._Universal.ApiClient.Peer.Direct;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
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
/// <item>The original's <c>CanTransfer_Unencrypted_Comment</c> and
/// <c>CanTransfer_Encrypted_Comment_S2110</c> asserted exactly the same things with the encryption
/// flags flipped, so they are one <c>[TestCase]</c>-per-flag test here. <c>S2110</c> was never
/// encryption-specific — both originals documented "Should succeed (S2110)" — so it stays in the
/// collapsed name.</item>
/// <item>The <c>…_AndUpdate_…</c> pair is <b>not</b> collapsed: the encrypted one passes a
/// <c>versionTag</c> on the overwrite and the unencrypted one does not, and the encrypted one alone
/// asserts <c>TransitCreated</c>/<c>TransitUpdated</c>. Collapsing would have to add or drop
/// coverage, so both survive over the shared arrange helper.</item>
/// <item>Only <c>CanTransfer_AndUpdate_Unencrypted_Comment</c> skips the <c>OriginalAuthor</c>
/// assertion on the pre-update comment — the other four make it. That asymmetry is the original's;
/// it is why the assertion sits at the call sites rather than in
/// <see cref="TransferCommentToFreshDrive"/>.</item>
/// </list>
/// Carried defect: <see cref="UniversalPeerDirectApiClient.DeleteFile"/> discards its
/// <c>ApiResponse</c>, so <c>CanDelete_Unencrypted_Comment</c> never checks that the delete request
/// was accepted — it only observes the resulting file state. Left as-is.
/// </remarks>
[TestFixture]
public class PeerDirectSendTests : V2Fixture
{
    private const DrivePermission CommentDrivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;

    private const string StandardFileContent = "We eagles fly to Mordor, sup w/ that?";
    private const string CommentFileContent = "Srsly!?? =O";
    private const string UpdatedCommentFileContent = "Bruh! Srsly!?? =O";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    /*
     Success Test - Comment
        Valid ReferencedFile (global transit id)
        Sender has storage Key (read access)
        Sender has write access
        Upload standard file  - encrypted per the test case
        Upload comment file   - encrypted per the test case
        Should succeed (S2110)
            Direct write comment
            Comment is not distributed
            ReferencedFile summary updated
            ReferencedFile is distributed to followers
     */
    [TestCase(false)]
    [TestCase(true)]
    public async Task CanTransfer_Comment_S2110(bool encrypted)
    {
        var sender = await LoginAsOwner(Identities.Frodo); //sender is the one who sends the comment
        var recipient = await LoginAsOwner(Identities.Sam);

        var scenario = await TransferCommentToFreshDrive(sender, recipient, encrypted);

        Assert.That(scenario.ReceivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));

        //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?
    }

    /// <summary>
    /// The S2110 flow plus an overwrite of the delivered comment, carrying the version tag the
    /// recipient reported.
    /// </summary>
    [Test]
    public async Task CanTransfer_AndUpdate_Encrypted_Comment_S2110()
    {
        const bool encrypted = true;

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var scenario = await TransferCommentToFreshDrive(sender, recipient, encrypted);
        var receivedFile = scenario.ReceivedFile;

        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));

        // UnixTimeUtc is not IComparable — compare .milliseconds (see the V2 README).
        Assert.That(receivedFile.FileMetadata.TransitCreated.milliseconds, Is.GreaterThan(0));
        Assert.That(receivedFile.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(0));

        //Sender updates their comment

        var (_, encryptedUpdatedCommentJsonContent64) = await TransferComment(
            sender,
            scenario.StandardFile.GlobalTransitIdFileIdentifier,
            uploadedContent: UpdatedCommentFileContent,
            encrypted: encrypted,
            recipient: recipient,
            overwriteFile: scenario.RemoteComment.GlobalTransitId,
            versionTag: receivedFile.FileMetadata.VersionTag);

        var updatedReceivedFile = await ReadSingleComment(recipient, scenario.Query);
        AssertCommentHeader(updatedReceivedFile, sender, encrypted, encryptedUpdatedCommentJsonContent64,
            scenario.RemoteComment.GlobalTransitId);
        Assert.That(updatedReceivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));

        Assert.That(updatedReceivedFile.FileMetadata.TransitCreated.milliseconds, Is.GreaterThan(0));
        Assert.That(updatedReceivedFile.FileMetadata.TransitUpdated.milliseconds, Is.EqualTo(0));
    }

    /// <summary>
    /// The S2110 flow plus an overwrite of the delivered comment, without a version tag.
    /// </summary>
    [Test]
    public async Task CanTransfer_AndUpdate_Unencrypted_Comment()
    {
        const bool encrypted = false;

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var scenario = await TransferCommentToFreshDrive(sender, recipient, encrypted);

        //Sender updates their comment

        await TransferComment(
            sender,
            scenario.StandardFile.GlobalTransitIdFileIdentifier,
            uploadedContent: UpdatedCommentFileContent,
            encrypted: encrypted,
            recipient: recipient,
            overwriteFile: scenario.RemoteComment.GlobalTransitId);

        var updatedReceivedFile = await ReadSingleComment(recipient, scenario.Query);
        AssertCommentHeader(updatedReceivedFile, sender, encrypted, UpdatedCommentFileContent,
            scenario.RemoteComment.GlobalTransitId);
        Assert.That(updatedReceivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
    }

    [Test]
    public async Task CanDelete_Unencrypted_Comment()
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var scenario = await TransferCommentToFreshDrive(sender, recipient, encrypted: false);

        Assert.That(scenario.ReceivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));

        //
        //Delete the comment
        //

        await sender.V1.PeerDirect.DeleteFile(
            FileSystemType.Comment,
            scenario.RemoteComment,
            [recipient.Identity]);

        await sender.Sync.DrainOutboxAsync();

        //
        // See the comment is deleted
        //

        var theDeletedFile = await ReadSingleComment(recipient, scenario.Query);
        Assert.That(theDeletedFile.FileState, Is.EqualTo(FileState.Deleted));
        Assert.That(theDeletedFile.FileSystemType, Is.EqualTo(FileSystemType.Comment));
    }

    /// <summary>
    /// What <see cref="TransferCommentToFreshDrive"/> leaves behind: the recipient's own post, the
    /// identifier the comment landed under on the recipient, the query that finds it again, and the
    /// comment header as the recipient's drive has it.
    /// </summary>
    private sealed record CommentScenario(
        UploadResult StandardFile,
        GlobalTransitIdFileIdentifier RemoteComment,
        QueryBatchRequest Query,
        SharedSecretEncryptedFileHeader ReceivedFile);

    /// <summary>
    /// The arrange-plus-assert block every test here opens with: connect the two identities on a fresh
    /// comment drive, have the recipient post a standard file, confirm the recipient has it, then have
    /// the sender direct-write a comment against it and confirm the comment is on the recipient's
    /// drive — with no <c>ProcessInboxAsync</c> anywhere, which is the property under test.
    /// </summary>
    private static async Task<CommentScenario> TransferCommentToFreshDrive(
        OwnerSession sender, OwnerSession recipient, bool encrypted)
    {
        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, CommentDrivePermissions,
            "comment target", allowAnonymousReads: false);

        var (standardFileUploadResult, encryptedStandardJsonContent64) =
            await UploadStandardFile(recipient, targetDrive, StandardFileContent, encrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGtidResponse = await recipient.V1.Drive.QueryByGlobalTransitId(
            standardFileUploadResult.GlobalTransitIdFileIdentifier);

        var recipientFileByGlobalTransitId = recipientFileByGtidResponse.Content?.SearchResults.SingleOrDefault();
        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content,
            Is.EqualTo(encryptedStandardJsonContent64 ?? StandardFileContent));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(encrypted));

        // Sender replies with a comment
        var (commentTransitResult, encryptedCommentJsonContent64) = await TransferComment(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: CommentFileContent,
            encrypted: encrypted,
            recipient: recipient);

        Assert.That(commentTransitResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        //

        // File should be on recipient server and accessible by global transit id
        var remoteComment = commentTransitResult.RemoteGlobalTransitIdFileIdentifier;
        var query = CommentQuery(remoteComment);
        var receivedFile = await ReadSingleComment(recipient, query);
        AssertCommentHeader(receivedFile, sender, encrypted,
            encryptedCommentJsonContent64 ?? CommentFileContent, remoteComment.GlobalTransitId);

        return new CommentScenario(standardFileUploadResult, remoteComment, query, receivedFile);
    }

    /// <summary>Finds one comment on the recipient's drive by its global transit id.</summary>
    private static QueryBatchRequest CommentQuery(GlobalTransitIdFileIdentifier remoteComment) => new()
    {
        QueryParams = new FileQueryParamsV1
        {
            TargetDrive = remoteComment.TargetDrive,
            GlobalTransitId = [remoteComment.GlobalTransitId]
        },
        ResultOptionsRequest = new QueryBatchResultOptionsRequest
        {
            MaxRecords = 10,
            IncludeMetadataHeader = true
        }
    };

    private static async Task<SharedSecretEncryptedFileHeader> ReadSingleComment(
        OwnerSession recipient, QueryBatchRequest query)
    {
        var batchResponse = await recipient.V1.Drive.QueryBatch(query, FileSystemType.Comment);
        var batch = batchResponse.Content;
        Assert.That(batch.SearchResults.Count(), Is.EqualTo(1));
        return batch.SearchResults.First();
    }

    private static void AssertCommentHeader(
        SharedSecretEncryptedFileHeader file,
        OwnerSession sender,
        bool encrypted,
        string expectedContent,
        Guid expectedGlobalTransitId)
    {
        Assert.That(file.FileState, Is.EqualTo(FileState.Active));
        Assert.That(file.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(file.FileMetadata.IsEncrypted, Is.EqualTo(encrypted));
        Assert.That(file.FileMetadata.AppData.Content, Is.EqualTo(expectedContent));
        Assert.That(file.FileMetadata.GlobalTransitId, Is.EqualTo(expectedGlobalTransitId));
    }

    /// <summary>
    /// Sends a comment file to a single recipient and performs basic assertions required by all tests,
    /// then drains the sender's outbox so the comment has actually gone out.
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

        Assert.That(transitResultResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
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
}
