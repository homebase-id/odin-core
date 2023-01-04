using Youverse.Core.Services.AppNotifications.ClientNotifications;

namespace Youverse.Core.Services.AppNotifications;

public class EstablishConnectionResponse : IClientNotification
{
    public ClientNotificationType NotificationType { get; } = ClientNotificationType.DeviceHandshakeSuccess;
}