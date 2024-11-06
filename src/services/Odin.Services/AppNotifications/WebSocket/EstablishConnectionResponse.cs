using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.ClientNotifications;

namespace Odin.Services.AppNotifications.WebSocket;

public class EstablishConnectionResponse : IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceHandshakeSuccess;
    public Guid NotificationTypeId { get; }

    public string GetClientData()
    {
        return "";
    }
}