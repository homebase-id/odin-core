using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationOutboxRecord
{
    public OdinId SenderId { get; set; }
    public AppNotificationOptions Options { get; set; }
    public UnixTimeUtc Timestamp { get; set; }
}