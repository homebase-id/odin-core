using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.ClientNotifications;

namespace Odin.Services.AppNotifications.WebSocket;

public class EstablishConnectionResponse : IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceHandshakeSuccess;
    public Guid NotificationTypeId { get; }

    /// <summary>
    /// The STUN URLs this device should put into its ICE servers, all of them, as given: its own
    /// server's names, which are not necessarily the identity name (docs/stun.md). Opaque to the
    /// client; refreshed on every connect, which is how a cluster change reaches devices without
    /// an extra call. Null on a peer socket, where the server is another identity's.
    /// </summary>
    public IReadOnlyList<string> StunUrls { get; init; }

    public string GetClientData()
    {
        return "";
    }
}
