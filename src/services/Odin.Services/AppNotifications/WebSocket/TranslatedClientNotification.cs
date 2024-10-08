using System;
using Odin.Services.AppNotifications.ClientNotifications;

namespace Odin.Services.AppNotifications.WebSocket;

public class TranslatedClientNotification : IClientNotification
{
    private readonly string _data;

    public TranslatedClientNotification(ClientNotificationType type, string data)
    {
        NotificationType = type;
        _data = data;
    }

    public ClientNotificationType NotificationType { get; set; }
    public Guid NotificationTypeId { get; }

    public string GetClientData()
    {
        return _data;
    }
}