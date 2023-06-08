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