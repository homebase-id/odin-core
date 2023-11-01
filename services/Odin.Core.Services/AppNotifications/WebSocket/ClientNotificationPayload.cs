namespace Odin.Core.Services.AppNotifications.WebSocket;

public class ClientNotificationPayload
{
    public bool IsEncrypted { get; set; }
    
    public string Payload { get; set; }

}