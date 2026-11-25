using System;
using Odin.Core;
using Odin.Core.Identity;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

/// <summary>
/// The outbox key of a notice to one recipient that carries no file of its own (an introduction, a connect for an
/// introducee, a disconnect, a request withdrawal): one per type per recipient, on the transient drive.  The outbox
/// is unique on the key and recipient and these are upserted, so a notice queued again replaces its own pending one,
/// and one of another type to the same person stays (#1824).
/// </summary>
public static class RecipientNoticeKey
{
    public static InternalDriveFileId For(OutboxItemType type, OdinId recipient) => new()
    {
        DriveId = SystemDriveConstants.TransientTempDrive.Alias,
        // Stored, so it must stay the same across releases: an explicit hash, not GuidId.FromString (not for static keys)
        FileId = new Guid(ByteArrayUtil.ReduceSHA256Hash($"{(int)type}:{recipient.DomainName}".ToUtf8ByteArray()))
    };
}
