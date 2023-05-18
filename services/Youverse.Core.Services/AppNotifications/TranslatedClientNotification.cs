using Youverse.Core.Services.AppNotifications.ClientNotifications;

namespace Youverse.Core.Services.AppNotifications;

public class TranslatedClientNotification : IClientNotification
{
    private readonly string _data;

    public TranslatedClientNotification(ClientNotificationType type, string data)
    {
        NotificationType = type;
        _data = data;
    }

    public ClientNotificationType NotificationType { get; set; }

    public string GetClientData()
    {
        return _data;
    }
}

public class ClientNotificationPayload
{
    public ClientNotificationPayload()
    {
    }
    
    public bool IsEncrypted { get; set; }
    
    public string Payload { get; set; }

}