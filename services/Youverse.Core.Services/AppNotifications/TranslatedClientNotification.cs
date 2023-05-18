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
    private readonly string _data;

    public ClientNotificationPayload(ClientNotificationType type, string data)
    {
        NotificationType = type;
        _data = data;
    }
    
    
    public bool Encrypted { get; set; }

    public ClientNotificationType NotificationType { get; set; }

    public string GetClientData()
    {
        return _data;
    }
}