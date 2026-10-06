using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Serialization;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Services.Peer;
using Odin.Services.Peer.Incoming.Drive.Transfer;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox;

#nullable enable

namespace Odin.Services.Registry.PayloadMove;

/// <summary>
/// The Inbox and Outbox items an identity move carries (#1871), and the files whose payloads they need. The
/// transfer fetches those before anything else, and the target does not resume until it has, so nothing is
/// processed or sent before its payloads are here.
/// </summary>
public static class PayloadMoveQueues
{
    private const int PageSize = 100;

    public const string FolderStagedReason =
        "queued before #1568, its files are in the source's inbox folder, which does not move";

    /// <summary>Why an Inbox row cannot come along on a move, or null if it can.</summary>
    public static string? WhyInboxItemStaysBehind(InboxRecord record)
    {
        try
        {
            return PeerInboxProcessor.HasFolderStagedFiles(TransitInboxBoxStorage.FromRecord(record)) ? FolderStagedReason : null;
        }
        catch (Exception e)
        {
            return $"its item could not be read ({e.GetType().Name})";
        }
    }

    /// <summary>
    /// The files whose payloads this identity's queued items need, as the payload transfer reads them:
    /// <list type="bullet">
    /// <item>an Inbox item that a peer streamed payloads with (a new file or an update), under the incoming
    /// fileId it holds. That file is not in the drive index until the item is processed, so the newest-first
    /// walk never sees it. A feed item carries metadata only; its payloads stay with the sender.</item>
    /// <item>the drive file an Outbox item sends.</item>
    /// </list>
    /// </summary>
    public static async Task<List<FilePayloadRow>> QueuedFilesAsync(IdentityDatabase db)
    {
        var files = new List<FilePayloadRow>();

        long? cursor = null;
        do
        {
            (var rows, cursor) = await db.Inbox.PagingByRowIdAsync(PageSize, cursor);
            foreach (var row in rows.Where(row => WhyInboxItemStaysBehind(row) == null))
            {
                var item = TransitInboxBoxStorage.FromRecord(row);
                if (CarriesStreamedPayloads(item))
                {
                    files.Add(new FilePayloadRow(row.rowId, item.DriveId, item.FileId,
                        OdinSystemSerializer.Serialize(item.FileMetadata)));
                }
            }
        } while (cursor != null);

        var sent = new HashSet<(Guid driveId, Guid fileId)>();
        cursor = null;
        do
        {
            (var rows, cursor) = await db.Outbox.PagingByRowIdAsync(PageSize, cursor);
            foreach (var row in rows.Where(row => (OutboxItemType)row.type is OutboxItemType.File or OutboxItemType.RemoteFileUpdate))
            {
                if (sent.Add((row.driveId, row.fileId)) && await db.DriveMainIndex.GetFilePayloadRowAsync(row.driveId, row.fileId) is { } file)
                {
                    files.Add(file);
                }
            }
        } while (cursor != null);

        return files;
    }

    private static bool CarriesStreamedPayloads(TransferInboxItem item) =>
        item.FileMetadata != null &&
        (item.InstructionType == TransferInstructionType.UpdateFile ||
         item is { InstructionType: TransferInstructionType.SaveFile, TransferFileType: not TransferFileType.EncryptedFileForFeed });
}
