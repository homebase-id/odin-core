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
    /// Do not play a sound or vibrate the phone
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
}