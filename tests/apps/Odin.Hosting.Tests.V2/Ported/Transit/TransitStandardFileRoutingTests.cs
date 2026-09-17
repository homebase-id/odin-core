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
/// Port of tests/apps/Odin.Hosting.Tests/OwnerApi/Transit/Inbox/TransitStandardFileRoutingTests.cs
///
/// Transit routing for standard files, per
/// https://github.com/YouFoundation/stories-and-architecture-docs/blob/master/concepts/transit/transit_routing.md:
/// an unencrypted file the sender can read is written straight onto the recipient's drive (S1110); an
/// encrypted one, where the sender holds no storage key on the recipient's drive, goes to the inbox
/// instead (S1210 / S1220); and a sender without write access is refused (S1010).
/// </summary>
/// <remarks>
/// Port notes:
/// <list type="bullet">
///   <item><description>
///     The original's <c>PrepareScenario</c> — create the drive on both identities, give the recipient
///     a circle granting the sender <c>drivePermissions</c>, connect — is
///     <see cref="PeerFlow.CreatePeerDriveAsync"/>. Its trailing assertion that the recipient's ICR
///     carries the expected circle grant was setup validation rather than a claim about routing;
///     <see cref="PeerFlow.ConnectAsync"/> asserts both connection calls succeeded instead.
///   </description></item>
///   <item><description>
///     <c>WaitForEmptyOutbox</c> became <c>Sync.DrainOutboxAsync</c> — the V1 call is a passive poll
///     that needs the outbox background service, which this host registers but never starts.
///     <c>WaitForTransferStatus</c> polls the same way, so <see cref="S1010"/> now drains and then
///     asserts the settled status once (see <see cref="DriveAsserts.AssertTransferStatus"/>).
///   </description></item>
///   <item><description>
///     The trailing <c>DeleteScenario</c> (disconnect the two identities) is gone: per-test reset
///     already restores that, and it asserted nothing.
///   </description></item>
///   <item><description>
///     No caller matrix and no <c>SetupCallerWithOwner</c> — these are owner-only flows, so the
///     drive-create / caller-build ordering question does not arise.
///   </description></item>
/// </list>
/// </remarks>
[TestFixture]
public class TransitStandardFileRoutingTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CanTransfer_Unencrypted_StandardFileAndDirectWrite_S1110()
    {
        /*
            Success Test - Standard File
                Upload standard file - encrypted = false
                Sender has write access
                Sender has storage key and read access
                Should succeed
                Perform direct write (S1110)
                File is distributed to followers -TODO: need to figure out distribution stuff
        */

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.ReadWrite;
        const string uploadedContent = "We eagles fly to Mordor, sup w/ that?";
        const bool isEncrypted = false;

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, drivePermissions,
            label: "Target drive", allowAnonymousReads: false);
        var (uploadResult, _) = await SendStandardFileAsync(sender, targetDrive, uploadedContent, encrypted: isEncrypted, recipient);

        Assert.That(uploadResult.RecipientStatus, Does.ContainKey((string)recipient.Identity));
        Assert.That(uploadResult.RecipientStatus[recipient.Identity], Is.EqualTo(TransferStatus.Enqueued));
        await sender.Sync.DrainOutboxAsync();
        //
        // Test results
        //

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
        //

        // File should be on recipient server and accessible by global transit id
        var receivedFile = await TransitScenario.SingleByGlobalTransitIdAsync(recipient, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));

        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(isEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(uploadedContent));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId, Is.EqualTo(uploadResult.GlobalTransitId));

        //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?
    }

    [Test]
    public async Task CanTransfer_Encrypted_StandardFileAndMoveToInbox_S1210_and_S1220()
    {
        /*
            Success Test - Standard File
                Upload standard file - encrypted = true
                Sender has write access
                sender does not have storage key
                Should succeed
                File goes to inbox (S1210, S1220)
         */

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Write;
        const string uploadedContent = "Three of us eagles, coming to save Frodo, Sam, and Smegol";
        const bool isEncrypted = true;

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, drivePermissions,
            label: "Target drive", allowAnonymousReads: false);
        var (uploadResult, encryptedJsonContent64) = await SendStandardFileAsync(sender,
            targetDrive, uploadedContent, encrypted: isEncrypted, recipient);

        Assert.That(uploadResult.RecipientStatus, Does.ContainKey((string)recipient.Identity));
        Assert.That(uploadResult.RecipientStatus[recipient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        await sender.Sync.DrainOutboxAsync();

        //
        //  Assert recipient does not have the file when it is first sent
        //
        var emptyBatch = await TransitScenario.QueryByGlobalTransitIdAsync(recipient, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(emptyBatch, Is.Empty);

        //
        await recipient.Sync.ProcessInboxAsync(targetDrive);
        //

        // Now the File should be on recipient server and accessible by global transit id
        var receivedFile = await TransitScenario.SingleByGlobalTransitIdAsync(recipient, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(receivedFile.FileState, Is.EqualTo(FileState.Active));
        Assert.That(receivedFile.FileMetadata.SenderOdinId, Is.EqualTo((string)sender.Identity));
        Assert.That(receivedFile.FileMetadata.OriginalAuthor, Is.EqualTo(sender.Identity));
        Assert.That(receivedFile.FileMetadata.IsEncrypted, Is.EqualTo(isEncrypted));
        Assert.That(receivedFile.FileMetadata.AppData.Content, Is.EqualTo(encryptedJsonContent64));
        Assert.That(receivedFile.FileMetadata.GlobalTransitId, Is.EqualTo(uploadResult.GlobalTransitId));

        //Assert - file was distributed to followers: TODO: decide if i want to test this here or else where?
    }

    [Test]
    public async Task FailsWhenSenderCannotWriteToTargetDriveOnRecipientServer_S1010()
    {
        /*
         Failure Test - Standard
            Fails when sender cannot write to target drive on recipients server
            Upload standard file - encrypted = true
            Sender does not have write access
            sender has  storage key
            Should succeed
            Throws 403
         */

        var sender = await LoginAsOwner(Identities.Frodo);
        var recipient = await LoginAsOwner(Identities.Sam);

        const DrivePermission drivePermissions = DrivePermission.Read;
        const string uploadedContent = "But we only got Frodo and Sam, thank you Smegol";
        const bool isEncrypted = false;

        var targetDrive = await PeerFlow.CreatePeerDriveAsync(sender, recipient, drivePermissions,
            label: "Target drive", allowAnonymousReads: false);
        var (uploadResult, _) = await SendStandardFileAsync(sender, targetDrive, uploadedContent, encrypted: isEncrypted, recipient);

        Assert.That(uploadResult.RecipientStatus, Does.ContainKey((string)recipient.Identity));
        Assert.That(uploadResult.RecipientStatus[recipient.Identity], Is.EqualTo(TransferStatus.Enqueued));

        //
        // Test results
        //

        //
        // Validate the transfer history was updated correctly
        //
        await sender.Sync.DrainOutboxAsync();
        await DriveAsserts.AssertTransferStatus(sender, uploadResult.File, recipient.Identity,
            LatestTransferStatus.RecipientIdentityReturnedAccessDenied);

        //IMPORTANT!!  the test here for direct write - meaning - the file should be on recipient server without calling process incoming files
        // recipientOwnerClient.Transit.ProcessIncomingInstructionSet(targetDrive);
        //

        // File should be on recipient server and accessible by global transit id
        var searchResults = await TransitScenario.QueryByGlobalTransitIdAsync(recipient, uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(searchResults, Is.Empty);
    }

    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Sends a standard file to a single recipient and performs basic assertions required by all tests
    /// </summary>
    private static async Task<(UploadResult UploadResult, string EncryptedJsonContent64)> SendStandardFileAsync(
        OwnerSession sender,
        TargetDrive targetDrive,
        string uploadedContent,
        bool encrypted,
        OwnerSession recipient)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = true,
            IsEncrypted = encrypted,
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
            Drive = targetDrive
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
                fileMetadata,
                storageOptions,
                transitOptions,
                fileSystemType: FileSystemType.Standard);
            encryptedJsonContent64 = content64;
            uploadResult = uploadResponse.Content;
        }
        else
        {
            var uploadResponse = await sender.V1.Drive.UploadNewMetadata(
                targetDrive,
                fileMetadata,
                transitOptions,
                FileSystemType.Standard);
            uploadResult = uploadResponse.Content;
        }

        //
        // Basic tests first which apply to all calls
        //
        Assert.That(uploadResult, Is.Not.Null);
        Assert.That(uploadResult!.RecipientStatus.Count, Is.EqualTo(1));

        return (uploadResult, encryptedJsonContent64);
    }
}
