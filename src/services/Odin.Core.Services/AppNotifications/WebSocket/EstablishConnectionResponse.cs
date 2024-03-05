using System;
using Odin.Core.Services.AppNotifications.ClientNotifications;

namespace Odin.Core.Services.AppNotifications.WebSocket;

public class EstablishConnectionResponse : IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceHandshakeSuccess;
    public Guid NotificationTypeId { get; }

    public string GetClientData()
    {
        return "";
    }
}