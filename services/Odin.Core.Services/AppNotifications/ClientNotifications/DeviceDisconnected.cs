using System;
using Odin.Core.Services.AppNotifications.WebSocket;

namespace Odin.Core.Services.AppNotifications.ClientNotifications
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