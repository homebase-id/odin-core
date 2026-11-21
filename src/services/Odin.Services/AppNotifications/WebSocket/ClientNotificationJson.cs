using Odin.Core.Serialization;

namespace Odin.Services.AppNotifications.WebSocket;

/// <summary>
/// A notification the server writes itself (an error, a refusal), as the client reads it inside a <see cref="ClientNotificationPayload"/>
/// </summary>
public static class ClientNotificationJson
{
    public static string Of(ClientNotificationType notificationType, string data) =>
        OdinSystemSerializer.Serialize(new
        {
            NotificationType = notificationType,
            Data = data,
        });
}
