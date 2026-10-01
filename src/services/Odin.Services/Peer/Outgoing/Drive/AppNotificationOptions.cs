using System;
using System.Collections.Generic;
using Odin.Core.Identity;

namespace Odin.Services.Peer.Outgoing.Drive;

/// <summary>
/// Options for notifying a recipient identity server
/// </summary>
public class AppNotificationOptions
{
    public Guid AppId { get; set; }

    public Guid TypeId { get; set; }

    /// <summary>
    /// An app-specific identifier
    /// </summary>
    public Guid TagId { get; set; }

    /// <summary>
    /// Background delivery: wake the app without showing anything. On iOS this is a background
    /// push (no alert, no sound); on Android every push is data-only and the app decides. Android
    /// and the notification list are unaffected by this flag.
    /// </summary>
    public bool Silent { get; set; }

    /// <summary>
    /// An app-specified field uses to filter what notification are allowed to be received from a peer identity
    /// </summary>
    public Guid PeerSubscriptionId { get; set; }

    /// <summary>
    /// If specified, the push notification should only be sent to this list of recipients (instead of any other list)
    /// </summary>
    public List<OdinId> Recipients { get; set; }

    public string UnEncryptedMessage { get; set; }

    /// <summary>
    /// Seconds after enqueue beyond which the push is dropped instead of delivered, on this server's
    /// outbox and on the device platform. Null: no expiry. A ring or a "send me your location" is
    /// worthless after a minute; a chat message is not.
    /// </summary>
    public int? TimeToLiveSeconds { get; set; }

    /// <summary>
    /// Platform collapse key (FCM collapse_key, apns-collapse-id, WebPush Topic): a later push with the
    /// same id replaces an undelivered earlier one, which is how "call ended" retracts "incoming call".
    /// At most 64 characters.
    /// </summary>
    public string CollapseId { get; set; }

    /// <summary>
    /// Deliver as a time-sensitive interruption where the platform supports it (iOS interruption-level;
    /// needs the app's entitlement, otherwise APNs downgrades it).
    /// </summary>
    public bool TimeSensitive { get; set; }
}