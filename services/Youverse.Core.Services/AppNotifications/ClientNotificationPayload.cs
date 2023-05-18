namespace Youverse.Core.Services.AppNotifications;

public class ClientNotificationPayload
{
    public bool IsEncrypted { get; set; }
    
    public string Payload { get; set; }

}