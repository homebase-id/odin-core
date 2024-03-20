#nullable enable
namespace Odin.Hosting.Controllers.OwnerToken.Notifications;

public class PushNotificationSubscribeFirebaseRequest
{
    public string FriendlyName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string DeviceToken { get; set; } = "";
}