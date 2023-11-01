#nullable enable
namespace Odin.Hosting.Controllers.OwnerToken.Notifications;

public class PushNotificationSubscribeDeviceRequest
{
    public string? FriendlyName { get; set; }
    public string? Endpoint { get; set; }
    
    public string? ExpirationTime { get; set; }
    public string? Auth { get; set; }
    public string? P256DH { get; set; }
    public string? GcmApiKey { get; set; }
}