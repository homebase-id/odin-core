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

    /// <summary>
    /// Optional: base64 of a 16-byte key. When set, a push for an incoming file carries the file's
    /// header sealed to this key (see PushPreviewSealer), for a handler that cannot fetch it yet.
    /// </summary>
    public string PreviewKey { get; set; } = "";
}
