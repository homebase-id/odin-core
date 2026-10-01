#nullable enable
namespace Odin.Hosting.Controllers.OwnerToken.Notifications;

public class PushNotificationSubscribeFirebaseRequest
{
    public string FriendlyName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public string DevicePlatform { get; set; } = "";

    /// <summary>
    /// Optional, iOS only: the PushKit VoIP token. It is a different token than
    /// <see cref="DeviceToken"/> and exists only while the app has the Voice over IP background
    /// mode. Register it only from a build that reports every VoIP push to CallKit; it is used
    /// solely for incoming calls (push kind Ring). Re-register whenever PushKit rotates it.
    /// </summary>
    public string VoipDeviceToken { get; set; } = "";
}
