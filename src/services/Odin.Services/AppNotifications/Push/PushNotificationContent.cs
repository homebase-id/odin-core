using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
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

    /// <summary>This device's sealed <see cref="PushPreview"/>, if it registered a preview key.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string EncryptedPreview { get; set; }

    /// <summary>Every device's sealed preview; never serialized, so no device sees another's.</summary>
    [JsonIgnore]
    public Dictionary<Guid, string> EncryptedPreviews { get; set; }

    public PushNotificationPayload ForDevice(Guid accessRegistrationId)
    {
        var copy = (PushNotificationPayload)MemberwiseClone();
        copy.EncryptedPreviews = null;
        copy.EncryptedPreview = EncryptedPreviews?.GetValueOrDefault(accessRegistrationId);
        return copy;
    }
}
