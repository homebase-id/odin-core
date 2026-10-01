using Odin.Core.Identity;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationOutboxRecord
{
    public OdinId SenderId { get; set; }
    public AppNotificationOptions Options { get; set; }
    public UnixTimeUtc Timestamp { get; set; }

    /// <summary>
    /// True when <see cref="AppNotificationOptions.TimeToLiveSeconds"/> is set and that many seconds
    /// have passed since <see cref="Timestamp"/>. An expired push is completed without sending rather
    /// than retried; a ring that arrives after the call ended is worse than none.
    /// </summary>
    public bool IsExpired(UnixTimeUtc now)
    {
        var ttl = Options?.TimeToLiveSeconds;
        if (ttl is null or <= 0)
        {
            return false;
        }

        return Timestamp.AddSeconds(ttl.Value) < now;
    }
}
