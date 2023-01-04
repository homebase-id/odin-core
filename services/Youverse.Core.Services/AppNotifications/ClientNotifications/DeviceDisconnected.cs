namespace Youverse.Core.Services.AppNotifications.ClientNotifications
{
    public class DeviceDisconnected :  IClientNotification
    {
        public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceDisconnected;

    }
}