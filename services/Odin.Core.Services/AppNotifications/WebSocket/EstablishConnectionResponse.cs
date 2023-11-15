using Odin.Core.Services.AppNotifications.ClientNotifications;

namespace Odin.Core.Services.AppNotifications.WebSocket;

public class EstablishConnectionResponse : IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceHandshakeSuccess;
    public string GetClientData()
    {
        return "";
    }
}