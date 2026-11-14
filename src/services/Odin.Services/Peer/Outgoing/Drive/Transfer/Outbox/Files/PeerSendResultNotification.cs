using System;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.Logging;
using Odin.Core.Identity;
using Odin.Core.Serialization;
using Odin.Services.AppNotifications.ClientNotifications;
using Odin.Services.AppNotifications.WebSocket;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Mediator;

namespace Odin.Services.Peer.Outgoing.Drive.Transfer.Outbox.Files;

/// <summary>
/// How a file or an update sent from the transient drive went at the recipient: the sender keeps no copy of it, so no
/// transfer history tells its client (the copy is deleted once sent).  A member's new file in, or change to, a file on
/// another identity's drive is sent this way, and so is their delete of one when it names the version it is meant for.  Heard on the sockets of the app that sent it, or the owner's own client's
/// (<see cref="IAppTargetedClientNotification"/>), matched to the send by the recipient, drive, global transit id and the
/// version tag the client got back.  Told before the outcome is recorded, so one may come again; the latest counts.
/// Nothing is kept for a client not connected at the time: it asks whether its send is still in the outbox
/// (<see cref="PeerOutbox.GetTransientSendStatusAsync"/>), and once not, reads the recipient's copy.
/// </summary>
public class PeerSendResultNotification : MediatorNotificationBase, IAppTargetedClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.PeerSendResult;

    public Guid NotificationTypeId { get; } = Guid.Parse("690e7080-fe41-4ec8-8a21-c887201c1bc8");

    /// <summary>The app that sent it, or <see cref="IAppTargetedClientNotification.OwnersOwnClient"/>: whose sockets hear it</summary>
    public Guid TargetAppId { get; init; }

    public OdinId Recipient { get; init; }

    /// <summary>The id of the recipient's drive the file was sent to (its alias)</summary>
    public Guid DriveId { get; init; }

    /// <summary>The global transit id the send named for the recipient's copy</summary>
    public Guid GlobalTransitId { get; init; }

    /// <summary>
    /// The version tag the client's upload or update gave back (<c>newVersionTag</c>), or the one its delete named
    /// (<c>expectedVersionTag</c>): which send of the file this is, when it sent more than one
    /// </summary>
    public Guid SentVersionTag { get; init; }

    /// <summary>An update of the recipient's file, not a new file</summary>
    public bool IsUpdate { get; init; }

    /// <summary>A delete of the recipient's file, not a new file</summary>
    public bool IsDelete { get; init; }

    /// <summary><see cref="LatestTransferStatus.Delivered"/>, or why not</summary>
    public LatestTransferStatus Status { get; init; }

    /// <summary>The send is tried again later (the recipient's server not answering, out of storage); otherwise final</summary>
    public bool WillRetry { get; init; }

    /// <summary>On delivery, the version tag of the recipient's copy, for the next change to name; null when the recipient's
    /// server didn't say, and for a delete</summary>
    public Guid? VersionTag { get; init; }

    public string GetClientData()
    {
        return OdinSystemSerializer.Serialize(new
        {
            Recipient = Recipient.DomainName,
            DriveId,
            GlobalTransitId,
            SentVersionTag,
            IsUpdate,
            IsDelete,
            Status,
            WillRetry,
            VersionTag
        });
    }

    /// <summary>
    /// Tells the sender's client how <paramref name="item"/> went, when it was sent from the transient drive
    /// (<see cref="OutboxItemState.SendResultTo"/>).  A failure to tell is logged and the outbox goes on.
    /// </summary>
    public static async Task PublishAsync(IMediator mediator, ILogger logger, OutboxFileItem item, LatestTransferStatus status,
        bool willRetry, Guid? versionTag)
    {
        if (mediator == null || item.State.SendResultTo is not { } to)
        {
            return;
        }

        try
        {
            await mediator.Publish(new PeerSendResultNotification
            {
                TargetAppId = to.AppId ?? IAppTargetedClientNotification.OwnersOwnClient,
                Recipient = item.Recipient,
                DriveId = to.DriveId,
                GlobalTransitId = to.GlobalTransitId,
                SentVersionTag = to.SentVersionTag,
                IsUpdate = item.Type == OutboxItemType.RemoteFileUpdate,
                IsDelete = item.Type == OutboxItemType.DeleteRemoteFile,
                Status = status,
                WillRetry = willRetry,
                VersionTag = versionTag
            });
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Failed telling the sender's client how file {file} to {recipient} went", item.File, item.Recipient);
        }
    }
}

/// <summary>Who hears how a send from the transient drive went, and of which copy of the recipient's</summary>
public class PeerSendResultTarget
{
    /// <summary>The app whose request queued it, null for the owner's own client</summary>
    public Guid? AppId { get; set; }

    /// <summary>The id of the recipient's drive (its alias)</summary>
    public Guid DriveId { get; set; }

    /// <summary>The global transit id the send named for the recipient's copy</summary>
    public Guid GlobalTransitId { get; set; }

    /// <summary>The version tag the client's upload or update gave back, or the one its delete named</summary>
    public Guid SentVersionTag { get; set; }

    public static PeerSendResultTarget Of(IOdinContext odinContext, Guid driveId, Guid? globalTransitId, Guid? sentVersionTag) => new()
    {
        AppId = AppIdOf(odinContext),
        DriveId = driveId,
        GlobalTransitId = globalTransitId.GetValueOrDefault(),
        SentVersionTag = sentVersionTag.GetValueOrDefault()
    };

    /// <summary>
    /// Whether this is the caller's send to <paramref name="driveId"/> of the file with <paramref name="globalTransitId"/>:
    /// the same app's, or the owner's own client's, and, when named, the send the client got
    /// <paramref name="sentVersionTag"/> back for
    /// </summary>
    public bool Matches(IOdinContext odinContext, Guid driveId, Guid globalTransitId, Guid? sentVersionTag) =>
        AppId == AppIdOf(odinContext) && DriveId == driveId && GlobalTransitId == globalTransitId &&
        (sentVersionTag == null || SentVersionTag == sentVersionTag);

    // Whose send it is: the app of the request, none for the owner's own client
    private static Guid? AppIdOf(IOdinContext odinContext) => odinContext.Caller.OdinClientContext?.AppId?.Value;
}
