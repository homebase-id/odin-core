using System;
using Odin.Core.Services.AppNotifications.WebSocket;

namespace Odin.Core.Services.AppNotifications.ClientNotifications
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