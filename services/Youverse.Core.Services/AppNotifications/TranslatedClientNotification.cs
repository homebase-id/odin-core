using Youverse.Core.Services.AppNotifications.ClientNotifications;

namespace Youverse.Core.Services.AppNotifications;

public class ClientNotification : IClientNotification
{
    public ClientNotificationType NotificationType { get; set; }
    public string GetClientData()
    {
        throw new System.NotImplementedException();
    }
}