using Odin.Core.Identity;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationOutboxRecord
{
    public OdinId SenderId { get; set; }
    public AppNotificationOptions Options { get; set; }
    public long Timestamp { get; set; }
}