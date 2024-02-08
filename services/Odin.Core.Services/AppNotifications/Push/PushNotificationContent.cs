using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Time;

namespace Odin.Core.Services.AppNotifications.Push;

public class PushNotificationContent
{
    public List<PushNotificationPayload> Payloads { get; set; }
}

public class PushNotificationPayload
{
    public OdinId SenderId { get; set; }
    public UnixTimeUtc Timestamp { get; set; }
    
    public string AppDisplayName { get; set; }

    public AppNotificationOptions Options { get; set; }
}
