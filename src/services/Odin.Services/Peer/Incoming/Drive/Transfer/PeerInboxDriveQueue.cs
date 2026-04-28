using System;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Peer.Incoming.Drive.Transfer;

// Tenant-scoped singleton queue of inbox-drain requests.
// Producers (e.g. query-batch endpoints) call Enqueue with the driveId and their
// request-scoped IOdinContext; we gate on caller authorization and clone the
// context internally so the background job survives the request scope.
// PeerInboxProcessorBackgroundService drains via TryDequeue.
public sealed class PeerInboxDriveQueue(ILogger<PeerInboxDriveQueue> logger)
{
    public readonly record struct Request(Guid DriveId, IOdinContext OdinContext);

    private readonly Channel<Request> _channel = Channel.CreateUnbounded<Request>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    // Returns true if the request was enqueued, false if the caller is not authorized
    // to drive inbox processing on this drive. Inbox processing applies pending writes
    // (soft-delete, read-receipt, reactions) on behalf of the drive owner; if a
    // low-privileged caller's context were used, ProcessInboxAsync would throw
    // OdinSecurityException on the underlying storage call and PeerInboxProcessor
    // would mark the inbox item DeleteFromInbox — silently dropping it. So we only
    // accept owner contexts that have ReadWrite on the target drive.
    public bool Enqueue(Guid driveId, IOdinContext odinContext)
    {
        if (!IsAuthorized(driveId, odinContext))
        {
            logger.LogDebug(
                "PeerInboxDriveQueue.Enqueue skipped: caller {caller} not authorized for ReadWrite on drive {driveId} (isOwner={isOwner})",
                odinContext.Caller?.OdinId, driveId, odinContext.Caller?.IsOwner);
            return false;
        }

        _channel.Writer.TryWrite(new Request(driveId, odinContext.Clone()));
        return true;
    }

    public bool TryDequeue(out Request request) => _channel.Reader.TryRead(out request);

    private static bool IsAuthorized(Guid driveId, IOdinContext odinContext)
    {
        if (odinContext?.Caller == null || odinContext.PermissionsContext == null)
        {
            return false;
        }

        return odinContext.Caller.IsOwner
               && odinContext.PermissionsContext.HasDrivePermission(driveId, DrivePermission.ReadWrite);
    }
}
