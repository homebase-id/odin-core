using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Drives.FileSystem;
using Odin.Services.Drives.FileSystem.Base;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

/// <summary>
/// A change of a file sent to this identity, its send again or its update, on every drive (docs/file-access.md, "Changing a
/// file you sent"): who may make it, and the version tag it names.
/// </summary>
internal static class SentFileChanges
{
    /// <summary>
    /// The context to change <paramref name="stored"/> in for <paramref name="sender"/>, and whether they sent it: on a
    /// collaborative channel any member who may read it; elsewhere its original sender, marked to change it without reading
    /// it while they may (<see cref="IDriveFileSystem"/>'s <c>SenderMayChangeAsync</c>), or with
    /// <paramref name="memberMayUseShares"/> on a Selective Share drive a member through their shares, which the header
    /// write asks.
    /// </summary>
    public static async Task<(IOdinContext Context, bool SentIt)> ContextForChangeAsync(IDriveFileSystem fs, StorageDrive drive,
        ServerFileHeader stored, OdinId sender, bool memberMayUseShares, IOdinContext odinContext)
    {
        if (stored.FileMetadata.FileState == FileState.Deleted)
        {
            throw new OdinSecurityException("File is deleted.");
        }

        var sentIt = SharedSecretEncryptedFileHeader.IsOriginalSender(stored.FileMetadata.SenderOdinId, sender);
        if (drive.IsCollaborationDrive())
        {
            return await fs.Storage.CallerMayReadFileOnLiveDrive(stored, odinContext)
                ? (odinContext, sentIt)
                : throw new OdinSecurityException($"Cannot access file with globalTransitId: {stored.FileMetadata.GlobalTransitId}");
        }

        if (sentIt)
        {
            return (await fs.Storage.SenderMayChangeAsync(drive, stored)
                ? OdinContextUpgrades.ForSendersOwnFile(odinContext, stored.FileMetadata.File, sender)
                : odinContext, true);
        }

        if (memberMayUseShares && drive.IsSelectiveShare)
        {
            return (odinContext, false);
        }

        throw new OdinSecurityException("Sender does not match original sender");
    }

    /// <summary>
    /// Whether the change already landed, the outbox sending it again: its sender's tag is the file's last write by them.
    /// Otherwise asserts it may go on: it names the version tag the file has, as a member who may read it does, or
    /// <paramref name="sentIt"/> and no one else has changed the file since its sender's last write, as a sender who can
    /// only write it changes it.  Anything else is refused with <see cref="OdinClientErrorCode.VersionTagMismatch"/>.
    /// </summary>
    public static bool AlreadyLandedOrAssertMayChange(ServerFileHeader stored, OdinId sender, bool sentIt, Guid sendersVersionTag,
        Guid? namedVersionTag)
    {
        var lastWrite = stored.FileMetadata.SendersWrite is { } last && last.Sender == sender ? last : null;
        if (lastWrite != null && lastWrite.SendersVersionTag == sendersVersionTag)
        {
            return true;
        }

        if (namedVersionTag == stored.FileMetadata.VersionTag || (sentIt && lastWrite?.VersionTag == stored.FileMetadata.VersionTag))
        {
            return false;
        }

        throw new OdinClientException($"File {stored.FileMetadata.File.FileId} was changed since its sender's last write; a change " +
                                      "names the version tag it has", OdinClientErrorCode.VersionTagMismatch);
    }

    /// <summary>
    /// The record of <paramref name="sender"/>'s write, whose own copy has <paramref name="sendersVersionTag"/>, giving the
    /// file <paramref name="versionTag"/>; null for a write with no tag of the sender's.
    /// </summary>
    public static SendersWrite SendersWriteOf(OdinId sender, Guid sendersVersionTag, Guid versionTag) =>
        sendersVersionTag != Guid.Empty
            ? new SendersWrite { Sender = sender, SendersVersionTag = sendersVersionTag, VersionTag = versionTag }
            : null;

    /// <summary>
    /// A change that landed before: done, nothing written.  An inbox item is marked complete, and the payloads it streamed
    /// in are deleted, as its write would have moved them; a direct write's staging is cleaned up by its caller.
    /// </summary>
    public static async Task<(bool success, List<PayloadDescriptor> payloads)> AlreadyDeliveredAsync(IDriveFileSystem fs,
        InternalDriveFileId incomingFile, List<PayloadDescriptor> incomingPayloads, WriteSecondDatabaseRowBase markComplete,
        IOdinContext odinContext)
    {
        if (markComplete is MarkInboxComplete)
        {
            if (await markComplete.ExecuteAsync() != 1)
            {
                throw new OdinSystemException("Hum, unable to mark the inbox record as completed, aborting");
            }

            await fs.Storage.CleanupAbandonedLongTermPayloads(incomingFile, incomingPayloads, odinContext);
        }

        return (true, []);
    }
}
