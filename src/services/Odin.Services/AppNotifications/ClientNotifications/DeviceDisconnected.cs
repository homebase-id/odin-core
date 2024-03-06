using System;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public class DeviceDisconnected :  IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceDisconnected;
        public Guid NotificationTypeId { get; }

        public string GetClientData()
        {
            return "";
        }
    }
}