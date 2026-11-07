using System;
using Odin.Core.Exceptions;
using Odin.Core.Storage;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

/// <summary>
/// Whether a peer's new file or update is written to the drive at once, or waits in the inbox for a key holder to sign in
/// (docs/terminology.md, "Files from others"): one rule for both.
/// </summary>
internal static class PeerDirectWrites
{
    /// <summary>
    /// At once unless the server would have to re-lock what arrives and the caller can't: an encrypted file, with a key
    /// header the server opens, on a Server Share drive whose key the caller doesn't hold.  A zero-knowledge drive has no
    /// inbox, and a key header passed on as sent (a deposit, a share's update, a comment on its file's key) needs no key.
    /// A comment never waits: it is refused instead.
    /// </summary>
    public static bool CanWriteAtOnce(StorageDrive drive, bool isEncrypted, EncryptedKeyHeader sentKeyHeader,
        FileSystemType fileSystemType, IOdinContext odinContext)
    {
        //HACK: if it's not a connected token
        if (!IsPeerToken(odinContext))
        {
            return false;
        }

        if (!isEncrypted || !drive.HasInbox || sentKeyHeader?.IsPassedOnAsSent() == true ||
            odinContext.PermissionsContext.HoldsDriveStorageKey(drive.Id))
        {
            return true;
        }

        //S2210 - comments cannot fall back to inbox
        if (fileSystemType == FileSystemType.Comment)
        {
            throw new OdinSecurityException($"Sender cannot write the comment to drive {drive.Id}");
        }

        return false;
    }

    internal static bool IsPeerToken(IOdinContext odinContext) =>
        string.Equals(odinContext.AuthContext, "TransitCapiAuthScheme", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(odinContext.AuthContext, "AutomatedIdentityAuthScheme", StringComparison.OrdinalIgnoreCase);
}
