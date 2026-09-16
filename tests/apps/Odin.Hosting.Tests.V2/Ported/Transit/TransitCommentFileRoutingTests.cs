using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Storage;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

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
///     <see cref="TransitScenario.AssertTransferStatusAsync"/>.
///   </description></item>
///   <item><description>
///     <b>Dropped, and not replaced:</b> the two <c>S2100</c> tests wrapped themselves in
///     <c>_scaffold.SetAssertLogEventsAction</c>, asserting that every <c>Error</c> log event raised
///     during the test read "Remote identity host failed: Referenced filed and metadata payload
///     encryption do not match". This framework has no log-event store or per-test log assertion hook,
///     so that half of those two tests is not carried; what remains is the transfer-history status
///     assertion, which is what the test names claim. Restoring it would mean giving
///     <c>V2Fixture</c> a log sink — see the framework-gap note in the batch report.
///   </description></item>
///   <item><description>
///     No caller matrix and no <c>SetupCallerWithOwner</c> — owner-only flows throughout.
///   </description></item>
/// </list>
/// Carried defect, behaviour left exactly as found:
/// <see cref="FailsWhenSenderCannotWriteCommentOnRecipientServer"/> asserts
/// <c>recipientStatus == TransferStatus.Enqueued</c> under the message "Should have been
/// RecipientReturnedAccessDenied" — the message describes the opposite of what is asserted. The
/// assertion is right (the upload enqueues; the refusal shows up later in the transfer history, which
/// the test goes on to check); the message is stale. NUnit prints both sides now, so the message is
/// gone rather than carried in its misleading form.
/// </remarks>
[TestFixture]
public class TransitCommentFileRoutingTests : V2Fixture
{
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = false;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = false;

        var targetDrive = await PrepareScenarioAsync(sender, recipient, drivePermissions);

        var (standardFileUploadResult, _) =
            await UploadStandardFileAsync(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitId = await GetByGlobalTransitIdAsync(recipient, standardFileUploadResult);

        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(standardFileContent));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(sender,
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
        // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
        //

        // File should be on recipient server and accessible by global transit id
        var searchResults = await TransitScenario.QueryByGlobalTransitIdAsync(recipient,
            commentUploadResult.GlobalTransitIdFileIdentifier, FileSystemType.Comment);
        Assert.That(searchResults.Count, Is.EqualTo(1));
        var receivedFile = searchResults.First();
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(commentFileContent));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId, Is.EqualTo(commentUploadResult.GlobalTransitId));

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

        const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = true;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = true;

        var targetDrive = await PrepareScenarioAsync(sender, recipient, drivePermissions);

        var (standardFileUploadResult, encryptedJsonContent64) =
            await UploadStandardFileAsync(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitId = await GetByGlobalTransitIdAsync(recipient, standardFileUploadResult);

        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentUploadResult, encryptedCommentJsonContent64) = await TransferCommentAsync(sender,
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
        // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
        //

        // File should be on recipient server and accessible by global transit id
        var searchResults = await TransitScenario.QueryByGlobalTransitIdAsync(recipient,
            commentUploadResult.GlobalTransitIdFileIdentifier, FileSystemType.Comment);
        Assert.That(searchResults.Count, Is.EqualTo(1));
        var receivedFile = searchResults.First();
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(commentIsEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedCommentJsonContent64));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId, Is.EqualTo(commentUploadResult.GlobalTransitId));

        //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Read;
        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = true;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = true;

        var targetDrive = await PrepareScenarioAsync(sender, recipient, drivePermissions);

        var (standardFileUploadResult, encryptedJsonContent64) =
            await UploadStandardFileAsync(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitId = await GetByGlobalTransitIdAsync(recipient, standardFileUploadResult);

        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentUploadResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        //
        // Validate the transfer history was updated correctly
        //
        await sender.Sync.DrainOutboxAsync();
        await TransitScenario.AssertTransferStatusAsync(sender, commentUploadResult.File, recipient.Identity,
            LatestTransferStatus.RecipientIdentityReturnedAccessDenied,
            FileSystemType.Comment);
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

        const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = true;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = true;

        var targetDrive = await PrepareScenarioAsync(sender, recipient, drivePermissions);

        await UploadStandardFileAsync(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        var invalidReferencedFile = new GlobalTransitIdFileIdentifier
        {
            GlobalTransitId = Guid.NewGuid(),
            TargetDrive = targetDrive
        };

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(sender,
            invalidReferencedFile,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentUploadResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        //
        // Validate the transfer history was updated correctly
        //
        await sender.Sync.DrainOutboxAsync();
        await TransitScenario.AssertTransferStatusAsync(sender, commentUploadResult.File, recipient.Identity,
            LatestTransferStatus.RecipientIdentityReturnedBadRequest,
            FileSystemType.Comment);
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = true;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = false;

        var targetDrive = await PrepareScenarioAsync(sender, recipient, drivePermissions);

        var (standardFileUploadResult, encryptedJsonContent64) =
            await UploadStandardFileAsync(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitId = await GetByGlobalTransitIdAsync(recipient, standardFileUploadResult);

        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentUploadResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        //
        // Validate the transfer history was updated correctly
        //
        await sender.Sync.DrainOutboxAsync();
        await TransitScenario.AssertTransferStatusAsync(sender, commentUploadResult.File, recipient.Identity,
            LatestTransferStatus.RecipientIdentityReturnedServerError,
            FileSystemType.Comment);
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Read | DrivePermission.WriteReactionsAndComments;
        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = false;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = true;

        var targetDrive = await PrepareScenarioAsync(sender, recipient, drivePermissions);

        var (standardFileUploadResult, _) =
            await UploadStandardFileAsync(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitId = await GetByGlobalTransitIdAsync(recipient, standardFileUploadResult);

        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(standardFileContent));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentUploadResult.RecipientStatus.TryGetValue(recipient.Identity, out var recipientStatus), Is.True);
        Assert.That(recipientStatus, Is.EqualTo(TransferStatus.Enqueued));

        //
        // Validate the transfer history was updated correctly
        //
        await sender.Sync.DrainOutboxAsync();
        await TransitScenario.AssertTransferStatusAsync(sender, commentUploadResult.File, recipient.Identity,
            LatestTransferStatus.RecipientIdentityReturnedServerError,
            FileSystemType.Comment);
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

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.WriteReactionsAndComments;
        const string standardFileContent = "We eagles fly to Mordor, sup w/ that?";
        const bool standardFileIsEncrypted = true;

        const string commentFileContent = "Srsly!?? =O";
        const bool commentIsEncrypted = true;

        var targetDrive = await PrepareScenarioAsync(sender, recipient, drivePermissions);

        var (standardFileUploadResult, encryptedJsonContent64) =
            await UploadStandardFileAsync(recipient, targetDrive, standardFileContent, standardFileIsEncrypted);

        //
        // Assert that the recipient server has the file by global transit id
        //
        var recipientFileByGlobalTransitId = await GetByGlobalTransitIdAsync(recipient, standardFileUploadResult);

        Assert.That(recipientFileByGlobalTransitId, Is.Not.Null);
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(recipientFileByGlobalTransitId.FileMetadata.IsEncrypted, Is.EqualTo(standardFileIsEncrypted));

        //sender replies with a comment
        var (commentUploadResult, _) = await TransferCommentAsync(sender,
            standardFileUploadResult.GlobalTransitIdFileIdentifier,
            uploadedContent: commentFileContent,
            encrypted: commentIsEncrypted, recipient);

        Assert.That(commentUploadResult.RecipientStatus.TryGetValue(recipient.Identity, out var transferStatus), Is.True);
        Assert.That(transferStatus, Is.EqualTo(TransferStatus.Enqueued));

        //
        // Validate the transfer history was updated correctly
        //
        await sender.Sync.DrainOutboxAsync();
        await TransitScenario.AssertTransferStatusAsync(sender, commentUploadResult.File, recipient.Identity,
            LatestTransferStatus.RecipientIdentityReturnedAccessDenied,
            FileSystemType.Comment);
    }

    // ---------------------------------------------------------------------------------------------

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

    private static Task<TargetDrive> PrepareScenarioAsync(
        OwnerSession sender, OwnerSession recipient, DrivePermission drivePermissions) =>
        PeerFlow.CreatePeerDriveAsync(sender, recipient, drivePermissions,
            label: "Target drive", allowAnonymousReads: false);

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

    /// <summary>The single file on <paramref name="owner"/>'s drive with that global transit id.</summary>
    private static async Task<SharedSecretEncryptedFileHeader> GetByGlobalTransitIdAsync(
        OwnerSession owner, UploadResult uploadResult)
    {
        var searchResults = await TransitScenario.QueryByGlobalTransitIdAsync(
            owner, uploadResult.GlobalTransitIdFileIdentifier);
        return searchResults.SingleOrDefault();
    }
}
