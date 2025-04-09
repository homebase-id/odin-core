using System;
using System.Collections.Generic;
using Odin.Core.Identity;
using Odin.Services.Peer;
using Odin.Services.Peer.Outgoing;
using Odin.Core.Time;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push;

public class PushNotificationContent
{
    public List<PushNotificationPayload> Payloads { get; set; }
}

public class PushNotificationPayload
{
    public OdinId SenderId { get; set; }
    
    public string AppDisplayName { get; set; }

    public UnixTimeUtc Timestamp { get; set; }

    public AppNotificationOptions Options { get; set; }
    
}
