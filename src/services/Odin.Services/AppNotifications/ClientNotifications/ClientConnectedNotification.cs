using System;
using Odin.Services.AppNotifications.WebSocket;

namespace Odin.Services.AppNotifications.ClientNotifications
{
    public class DeviceConnected : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceConnected;

        public Guid NotificationTypeId { get; } = Guid.Empty;

        public string SocketId { get; set; }
        
        public string GetClientData()
        {
            return "";
        }
    }
}