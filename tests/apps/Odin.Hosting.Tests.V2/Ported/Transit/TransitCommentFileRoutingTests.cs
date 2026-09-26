using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Transit;

/// <summary>
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/Transit/Inbox/TransitCommentFileRoutingTests.cs
///
/// Transit routing for comment files, per
/// https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/transit/transit_routing.md:
/// a comment on a file the sender can read is written straight onto the recipient's drive (S2110),
/// and the four ways that fails — no write access (S2010), a referenced file that does not exist
/// (S2030), comment encryption not matching the referenced file's (S2100), and an encrypted comment
/// from a sender with no storage key (S2210).
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     <c>PrepareScenario</c> is <see cref="PeerFlow.CreatePeerDriveAsync"/>; its trailing ICR
///     circle-grant assertion was setup validation, and <see cref="PeerFlow.ConnectAsync"/> asserts
///     the two connection calls succeeded instead. The trailing <c>DeleteScenario</c> disconnect is
///     dropped — per-test reset covers it and it asserted nothing.
///   </description></item>
///   <item><description>
///     <c>WaitForEmptyOutbox</c> / <c>WaitForTransferStatus</c> are passive polls that need the outbox
///     background service, which this host registers but never starts. Both became
///     <c>Sync.DrainOutboxAsync</c> followed, where a status was being waited for, by
///     <see cref="DriveAsserts.AssertTransferStatus"/>.
///   </description></item>
///   <item><description>
///     The four failure tests differed only in the drive permission, the two encryption flags and the
///     expected <c>LatestTransferStatus</c>, so they are the four rows of
///     <see cref="FailureCases"/>. The S-codes they exercise are in the row names.
///   </description></item>
///   <item><description>
///     <b>Dropped:</b> the two <c>S2100</c> tests wrapped themselves in
///     <c>_scaffold.SetAssertLogEventsAction</c> to tolerate the Error that the recipient logged when it
///     refused the comment with a 503. It refuses with a 400 now (#1771), which is not logged at Error,
///     so the fixture's no-Error invariant holds without any toleration.
///   </description></item>
///   <item><description>
///     No caller matrix and no <c>SetupCallerWithOwner</c> — owner-only flows throughout.
///   </description></item>
/// </list>
/// Carried defect, behaviour left exactly as found: the
/// <c>FailsWhenSenderCannotWriteCommentOnRecipientServer</c> row asserts that the comment
/// upload enqueues, under a message that read "Should have been RecipientReturnedAccessDenied" — the
/// message described the opposite of what was asserted. The assertion is right (the upload enqueues;
/// the refusal shows up later in the transfer history, which the row goes on to check); the message
/// was stale. NUnit prints both sides now, so the message is gone rather than carried in its
/// misleading form.
/// </remarks>
[TestFixture]
public class TransitCommentFileRoutingTests : V2Fixture
{
    private const string StandardFileContent = "We eagles fly to Mordor, sup w/ that?";
    private const string CommentFileContent = "Srsly!?? =O";

    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

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

        const bool commentIsEncrypted = false;

        var scenario = await ArrangeAsync(
            DrivePermission.Read | DrivePermission.WriteReactionsAndComments,
            standardFileIsEncrypted: false);

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(scenario.Sender,
            scenario.StandardFile.GlobalTransitIdFileIdentifier,
            uploadedContent: CommentFileContent,
            encrypted: commentIsEncrypted, scenario.Recipient);

        AssertEnqueuedFor(commentUploadResult, scenario.Recipient);

        await scenario.Sender.Sync.DrainOutboxAsync();

        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
        //

        // File should be on recipient server and accessible by global transit id
        await AssertCommentLandedAsync(scenario, commentUploadResult, CommentFileContent, commentIsEncrypted);

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

        const bool commentIsEncrypted = true;

        var scenario = await ArrangeAsync(
            DrivePermission.Read | DrivePermission.WriteReactionsAndComments,
            standardFileIsEncrypted: true);

        //sender replies with a comment
        var (commentUploadResult, encryptedCommentJsonContent64) = await TransferCommentAsync(scenario.Sender,
            scenario.StandardFile.GlobalTransitIdFileIdentifier,
            uploadedContent: CommentFileContent,
            encrypted: commentIsEncrypted, scenario.Recipient);

        AssertEnqueuedFor(commentUploadResult, scenario.Recipient);

        await scenario.Sender.Sync.DrainOutboxAsync();

        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
        //

        // File should be on recipient server and accessible by global transit id
        await AssertCommentLandedAsync(scenario, commentUploadResult, encryptedCommentJsonContent64, commentIsEncrypted);

        //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        // Unlike the other tests, this one never reads the standard file back: the comment deliberately
        // references a file that does not exist, so nothing is asserted about the one that does.
        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient,
            DrivePermission.Read | DrivePermission.WriteReactionsAndComments,
            label: "Target drive", allowAnonymousReads: false);

        await UploadStandardFileAsync(recipient, targetDrive, StandardFileContent, encrypted: true);

        var invalidReferencedFile = new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = Guid.NewGuid(),
            TargetDrive = targetDrive
        };

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(sender,
            invalidReferencedFile,
            uploadedContent: CommentFileContent,
            encrypted: true, recipient);

        AssertEnqueuedFor(commentUploadResult, recipient);

        //
        // Validate the transfer history was updated correctly
        //
        await sender.Sync.DrainOutboxAsync();
        await DriveAsserts.AssertTransferStatus(sender, commentUploadResult.File, recipient.Identity,
            LatestTransferStatus.RecipientIdentityReturnedBadRequest,
            FileSystemType.Comment);
    }

    /// <summary>
    /// The four ways a comment transfer is refused. Each row sends one comment on a valid referenced
    /// file and asserts the settled <see cref="LatestTransferStatus"/> in the sender's history.
    /// </summary>
    private static IEnumerable<TestCaseData> FailureCases()
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
        yield return new TestCaseData(DrivePermission.Read, true, true,
                LatestTransferStatus.RecipientIdentityReturnedAccessDenied)
            .SetName("FailsWhenSenderCannotWriteCommentOnRecipientServer");

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
        yield return new TestCaseData(DrivePermission.Read | DrivePermission.WriteReactionsAndComments, true, false,
                LatestTransferStatus.RecipientIdentityReturnedBadRequest)
            .SetName("FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test1");

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
        yield return new TestCaseData(DrivePermission.Read | DrivePermission.WriteReactionsAndComments, false, true,
                LatestTransferStatus.RecipientIdentityReturnedBadRequest)
            .SetName("FailsWhenEncryptionDoesNotMatchCommentAndReferencedFile_S2100_Test2");

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
        yield return new TestCaseData(DrivePermission.WriteReactionsAndComments, true, true,
                LatestTransferStatus.RecipientIdentityReturnedAccessDenied)
            .SetName("FailsWhenCommentFileIsEncryptedAndSenderHasNoDriveStorageKeyOnRecipientServer_S2210");
    }

    [TestCaseSource(nameof(FailureCases))]
    public async Task CommentTransferIsRefused(
        DrivePermission drivePermissions,
        bool standardFileIsEncrypted,
        bool commentIsEncrypted,
        LatestTransferStatus expectedStatus)
    {
        var scenario = await ArrangeAsync(drivePermissions, standardFileIsEncrypted);

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(scenario.Sender,
            scenario.StandardFile.GlobalTransitIdFileIdentifier,
            uploadedContent: CommentFileContent,
            encrypted: commentIsEncrypted, scenario.Recipient);

        AssertEnqueuedFor(commentUploadResult, scenario.Recipient);

        //
        // Validate the transfer history was updated correctly
        //
        await scenario.Sender.Sync.DrainOutboxAsync();
        await DriveAsserts.AssertTransferStatus(scenario.Sender, commentUploadResult.File, scenario.Recipient.Identity,
            expectedStatus,
            FileSystemType.Comment);
    }

    // ---------------------------------------------------------------------------------------------

    private sealed record CommentScenario(
        OwnerSession Sender,
        OwnerSession Recipient,
        UploadResult StandardFile);

    /// <summary>
    /// Frodo and Sam connected over one drive on which Frodo holds <paramref name="drivePermissions"/>,
    /// with one standard file of Sam's on it that Sam can find by its global transit id.
    /// </summary>
    private async Task<CommentScenario> ArrangeAsync(DrivePermission drivePermissions, bool standardFileIsEncrypted)
    {
        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, drivePermissions,
            label: "Target drive", allowAnonymousReads: false);

        var (standardFileUploadResult, encryptedJsonContent64) =
            await UploadStandardFileAsync(recipient, targetDrive, StandardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitId = await TransitScenario.SingleByGlobalTransitIdAsync(
            recipient, standardFileUploadResult.GlobalTransitIdFileIdentifier);

        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content,
            Is.EqualTo(encryptedJsonContent64 ?? StandardFileContent));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        return new CommentScenario(sender, recipient, standardFileUploadResult);
    }

    /// <summary>The upload enqueued one item, for <paramref name="recipient"/>.</summary>
    private static void AssertEnqueuedFor(UploadResult uploadResult, OwnerSession recipient)
    {
        Assert.That(uploadResult.RecipientStatus, Does.ContainKey((string)recipient.Identity));
        Assert.That(uploadResult.RecipientStatus[recipient.Identity], Is.EqualTo(TransferStatus.Enqueued));
    }

    /// <summary>
    /// The comment is on the recipient's drive, authored by the sender, carrying
    /// <paramref name="expectedContent"/>.
    /// </summary>
    private static async Task AssertCommentLandedAsync(
        CommentScenario scenario, UploadResult commentUploadResult, string expectedContent, bool commentIsEncrypted)
    {
        var receivedFile = await TransitScenario.SingleByGlobalTransitIdAsync(scenario.Recipient,
            commentUploadResult.GlobalTransitIdFileIdentifier, FileSystemType.Comment);

        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)scenario.Sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(scenario.Sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(expectedContent));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId, Is.EqualTo(commentUploadResult.GlobalTransitId));
    }

    /// <summary>
    /// Sends a comment file to a single recipient and performs basic assertions required by all tests
    /// </summary>
    private static async Task<(UploadResult UploadResult, string EncryptedJsonContent64)> TransferCommentAsync(
        OwnerSession sender,
        GlobalTransitIdFileIdentifier referencedFile,
        string uploadedContent,
        bool encrypted,
        OwnerSession recipient)
    {
        var fileMetadata = new UploadFileMetadata
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

        var storageOptions = new StorageOptions
        {
            Drive = referencedFile.TargetDrive
        };

        var transitOptions = new TransitOptions
        {
            Recipients = new List<string> { recipient.Identity },
            RemoteTargetDrive = default
        };

        UploadResult uploadResult;
        string encryptedJsonContent64 = null;
        if (encrypted)
        {
            var (uploadResponse, content64) = await sender.V1.Drive.UploadNewEncryptedMetadata(
                fileMetadata, storageOptions, transitOptions, fileSystemType: FileSystemType.Comment);
            encryptedJsonContent64 = content64;
            uploadResult = uploadResponse.Content;
        }
        else
        {
            var uploadResponse = await sender.V1.Drive.UploadNewMetadata(
                referencedFile.TargetDrive,
                fileMetadata,
                transitOptions,
                FileSystemType.Comment);
            uploadResult = uploadResponse.Content;
        }

        //
        // Basic tests first which apply to all calls
        //
        Assert.That(uploadResult, Is.Not.Null);
        Assert.That(uploadResult!.RecipientStatus.Count, Is.EqualTo(1));

        return (uploadResult, encryptedJsonContent64);
    }

    private static async Task<(UploadResult UploadResult, string EncryptedJsonContent64)> UploadStandardFileAsync(
        OwnerSession owner, TargetDrive targetDrive, string uploadedContent, bool encrypted)
    {
        var fileMetadata = new UploadFileMetadata
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

        if (encrypted)
        {
            var (encryptedResponse, encryptedJsonContent64) =
                await owner.V1.Drive.UploadNewEncryptedMetadata(targetDrive, fileMetadata);
            return (encryptedResponse.Content, encryptedJsonContent64);
        }

        var response = await owner.V1.Drive.UploadNewMetadata(targetDrive, fileMetadata);
        return (response.Content, null);
    }
}
