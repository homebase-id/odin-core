#nullable enable
namespace Odin.Hosting.Controllers.OwnerToken.Notifications;

public class PushNotificationSubscribeFirebaseRequest
{
    public string FriendlyName { get; set; } = "";
    public string Endpoint { get; set; } = "";
    public string DeviceToken { get; set; } = "";
    public string DevicePlatform { get; set; } = "";

    /// <summary>
    /// Optional, iOS only: the PushKit VoIP token, a different token than <see cref="DeviceToken"/>.
    /// Only a build that reports VoIP pushes to CallKit may register one (see PushKind.Ring).
    /// Re-register whenever PushKit rotates it.
    /// </summary>
    public string VoipDeviceToken { get; set; } = "";
}
