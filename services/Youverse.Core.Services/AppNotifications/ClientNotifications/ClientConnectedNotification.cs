namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class DeviceConnected : IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceConnected;

        public string SocketId { get; set; }
    }
}