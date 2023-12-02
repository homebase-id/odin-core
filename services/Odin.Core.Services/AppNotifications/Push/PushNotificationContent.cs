using Odin.Core.Identity;
using Odin.Core.Services.Peer;
using Odin.Core.Time;

namespace Odin.Core.Services.AppNotifications.Push;

public class PushNotificationContent
{
    public string Payload { get; set; }
}

public class PushNotificationPayload
{
    public OdinId SenderId { get; set; }
    public UnixTimeUtc Timestamp { get; set; }
    public AppNotificationOptions Options { get; set; }
}
