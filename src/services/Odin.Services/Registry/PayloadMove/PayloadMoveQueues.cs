using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

/// <summary>
/// The Inbox and Outbox items an identity move carries (#1871), and the payloads they need. The transfer fetches
/// those before anything else, and the target does not resume until it has, so nothing is processed or sent
/// before its payloads are here.
/// </summary>
public static class PayloadMoveQueues
{
    private const int PageSize = 100;

    /// <summary>Why an Inbox row cannot come along on a move, or null if it can.</summary>
    public static string? WhyInboxItemStaysBehind(InboxRecord record)
    {
        try
        {
            return PeerInboxProcessor.HasFolderStagedFiles(TransitInboxBoxStorage.FromRecord(record))
                ? "queued before #1568, its files are in the source's inbox folder, which does not move"
                : null;
        }
        catch (Exception e)
        {
            return $"its item could not be read ({e.GetType().Name})";
        }
    }

    /// <summary>
    /// The payloads and thumbnails this identity's queued items need:
    /// <list type="bullet">
    /// <item>an Inbox item's, under the incoming fileId it holds: its metadata rides on the row exactly when a
    /// peer streamed its payloads here. That file is not in the drive index until the item is processed, so the
    /// newest-first walk never sees it. A feed item's payloads are remote, so it has none.</item>
    /// <item>the drive file an Outbox item sends.</item>
    /// </list>
    /// A row or header that cannot be read is passed over here; the walk reports an unreadable header.
    /// </summary>
    public static async Task<List<PayloadObject>> QueuedObjectsAsync(IdentityDatabase db)
    {
        var objects = new List<PayloadObject>();

        long? cursor = null;
        do
        {
            (var rows, cursor) = await db.Inbox.PagingByRowIdAsync(PageSize, cursor);
            foreach (var row in rows)
            {
                if (TryRead(() => TransitInboxBoxStorage.FromRecord(row)) is { } item)
                {
                    objects.AddRange(PayloadObject.AllOf(item.DriveId, item.FileId, item.FileMetadata));
                }
            }
        } while (cursor != null);

        // One file sent to several recipients is one Outbox row each
        var sent = new HashSet<(Guid driveId, Guid fileId)>();
        cursor = null;
        do
        {
            (var rows, cursor) = await db.Outbox.PagingByRowIdAsync(PageSize, cursor);
            foreach (var row in rows)
            {
                if ((OutboxItemType)row.type is OutboxItemType.File or OutboxItemType.RemoteFileUpdate &&
                    sent.Add((row.driveId, row.fileId)) &&
                    await db.DriveMainIndex.GetFilePayloadRowAsync(row.driveId, row.fileId) is { FileMetaData: { } header } file)
                {
                    objects.AddRange(PayloadObject.AllOf(file.DriveId, file.FileId,
                        TryRead(() => OdinSystemSerializer.Deserialize<FileMetadata>(header))));
                }
            }
        } while (cursor != null);

        return objects;
    }

    private static T? TryRead<T>(Func<T?> read) where T : class
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
