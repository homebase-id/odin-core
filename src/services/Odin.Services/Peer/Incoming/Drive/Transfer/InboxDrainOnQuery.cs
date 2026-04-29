using System;
using System.Threading.Tasks;
using Odin.Services.Background;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Peer.Incoming.Drive.Transfer.InboxStorage;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

// Inline inbox drain triggered by user-facing query endpoints. Lets a query
// response include peer data that arrived just before the query landed, without
// waiting for the background processor. Cheap on the empty-inbox hot path
// because GetReadyCountAsync is cache-backed.
//
// We bound the inline work to InlineBatchLimit so a query response time can't
// blow up on a large backlog. If the inbox holds more than that, we hand the
// overflow to PeerInboxProcessorBackgroundService and return; the next query
// (or any other drain trigger) will keep chipping away.
public sealed class InboxDrainOnQuery(
    TransitInboxBoxStorage transitInboxBoxStorage,
    PeerInboxProcessor peerInboxProcessor,
    PeerInboxDriveQueue peerInboxDriveQueue,
    IBackgroundServiceNotifier<PeerInboxProcessorBackgroundService> peerInboxProcessorNotifier,
    IDriveManager driveManager)
{
    public const int InlineBatchLimit = 50;

    public async Task DrainIfReadyAsync(Guid driveId, IOdinContext odinContext)
    {
        var drive = await driveManager.GetDriveAsync(driveId);
        if (drive == null)
        {
            return;
        }
        
        // Same auth gate PeerInboxDriveQueue.Enqueue uses: inbox processing applies
        // owner-level writes, and ProcessInboxAsync would silently DeleteFromInbox
        // items if invoked under a low-privileged context (see comment on
        // IsAuthorizedToDrain). Skip the inline drain entirely for non-owner callers
        // and let the owner's next query (or any explicit /process-inbox call) do the work.
        if (!PeerInboxDriveQueue.IsAuthorizedToDrain(driveId, odinContext))
        {
            return;
        }

        var readyCount = await transitInboxBoxStorage.GetReadyCountAsync(driveId);
        if (readyCount <= 0)
        {
            return;
        }

        // Hand the overflow off first so the background can start working on it
        // while we do our inline pass.
        if (readyCount > InlineBatchLimit)
        {
            peerInboxDriveQueue.Enqueue(driveId, odinContext);
            await peerInboxProcessorNotifier.NotifyWorkAvailableAsync();
        }

        await peerInboxProcessor.ProcessInboxAsync(drive.TargetDriveInfo, odinContext, InlineBatchLimit);
    }
}
