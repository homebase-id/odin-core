using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer;
using Odin.Services.Peer.Encryption;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer;

namespace Odin.Hosting.Tests.V2.Ported.Peer;

/// <summary>
/// The upload-and-deliver scaffolding the ported transfer-history and read-receipt fixtures share:
/// one encrypted metadata-only file sent over peer transit, and the read receipt that comes back.
/// </summary>
/// <remarks>
/// Four fixtures carried their own copy of this (two of them byte-identical), differing only in what
/// they returned. The split is by what the caller needs, not by fixture:
/// <see cref="UploadEncryptedMetadataAsync"/> is the fan-out case — it stops at the sender's outbox,
/// because a multi-recipient test drives each recipient's inbox itself — while
/// <see cref="TransferEncryptedMetadataAsync"/> carries the single-recipient case all the way to the
/// recipient's copy of the file.
/// </remarks>
internal static class PeerTransferScenario
{
    /// <summary>The app content every one of these fixtures uploads.</summary>
    private const string UploadedContent = "pie";

    /// <summary>
    /// Upload one encrypted metadata-only file addressed to <paramref name="transitOptions"/>'
    /// recipients and drain the sender's outbox. Asserts the upload succeeded and reported a status
    /// per recipient; the per-recipient status itself is left to the caller, since the fan-out tests
    /// expect some of them to fail.
    /// </summary>
    /// <param name="allowDistribution">
    /// False reproduces the "file forbids distribution" case: every item fails and is rescheduled, so
    /// the outbox never empties and draining it is pointless (the caller drains anyway where the
    /// still-queued count is the thing under test).
    /// </param>
    public static async Task<(UploadResult UploadResult, KeyHeader KeyHeader, UploadFileMetadata FileMetadata)>
        UploadEncryptedMetadataAsync(
            OwnerSession sender,
            TargetDrive targetDrive,
            TransitOptions transitOptions,
            bool allowDistribution = true)
    {
        var fileMetadata = new UploadFileMetadata
        {
            AllowDistribution = allowDistribution,
            IsEncrypted = true,
            AppData = new()
            {
                Content = UploadedContent,
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

        // Supplied rather than left to the client: the client wipes a key header it minted itself, and
        // the resend test needs the AES key afterwards.
        var keyHeader = KeyHeader.NewRandom16();

        var (uploadResponse, _) = await sender.V1.Drive.UploadNewEncryptedMetadata(
            fileMetadata,
            storageOptions,
            transitOptions,
            keyHeader);

        Assert.That(uploadResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var uploadResult = uploadResponse.Content;
        Assert.That(uploadResult, Is.Not.Null);
        Assert.That(uploadResult.RecipientStatus.Count, Is.EqualTo(transitOptions.Recipients.Count));

        if (allowDistribution) // we will only have a chance to get an empty outbox if the file is distributed
        {
            await sender.Sync.DrainOutboxAsync();
        }

        return (uploadResult, keyHeader, fileMetadata);
    }

    /// <summary>
    /// <see cref="UploadEncryptedMetadataAsync"/> for a single recipient, carried through to the
    /// recipient's own copy: asserts the send was enqueued, processes the recipient's inbox and
    /// returns what landed there.
    /// </summary>
    public static async Task<(
            UploadResult UploadResult,
            SharedSecretEncryptedFileHeader RecipientFile,
            KeyHeader KeyHeader,
            UploadFileMetadata FileMetadata)>
        TransferEncryptedMetadataAsync(
            OwnerSession sender,
            OwnerSession recipient,
            TargetDrive targetDrive,
            TransitOptions transitOptions)
    {
        var (uploadResult, keyHeader, fileMetadata) =
            await UploadEncryptedMetadataAsync(sender, targetDrive, transitOptions);

        Assert.That(uploadResult.RecipientStatus[transitOptions.Recipients.Single()], Is.EqualTo(TransferStatus.Enqueued));

        // validate recipient got the file
        await recipient.Sync.ProcessInboxAsync(uploadResult.File.TargetDrive);
        var recipientFile = await GetRecipientCopyAsync(recipient, uploadResult);

        return (uploadResult, recipientFile, keyHeader, fileMetadata);
    }

    /// <summary>
    /// The recipient's copy of a transferred file. Found by global transit id — the recipient's own
    /// <c>FileId</c> differs from the sender's.
    /// </summary>
    public static async Task<SharedSecretEncryptedFileHeader> GetRecipientCopyAsync(
        OwnerSession recipient, UploadResult uploadResult)
    {
        var response = await recipient.V1.Drive.QueryByGlobalTransitId(uploadResult.GlobalTransitIdFileIdentifier);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var file = response.Content.SearchResults.SingleOrDefault();
        Assert.That(file, Is.Not.Null, $"recipient: {recipient.Identity}");
        return file;
    }

    /// <summary>Addresses a file header for the endpoints that take an external file identifier.</summary>
    public static ExternalFileIdentifier AsExternalFile(SharedSecretEncryptedFileHeader header) =>
        new()
        {
            FileId = header.FileId,
            TargetDrive = header.TargetDrive
        };

    /// <summary>
    /// The recipient marks <paramref name="file"/> read; the receipt is accepted for delivery back to
    /// the original sender. Leaves the receipt in the recipient's outbox — the caller decides whether
    /// it should reach the sender.
    /// </summary>
    public static async Task SendReadReceiptAsync(
        OwnerSession sender, OwnerSession recipient, ExternalFileIdentifier file)
    {
        var response = await recipient.V1.Drive.SendReadReceipt([file]);
        DriveAsserts.AssertReadReceiptStatus(response, file, sender.Identity, SendReadReceiptResultStatus.Enqueued);
    }
}
