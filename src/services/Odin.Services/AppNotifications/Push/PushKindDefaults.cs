#nullable enable
using System;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push;

/// <summary>
/// What a <see cref="PushKind"/> means in delivery terms. Applied once, where every push is
/// enqueued, so the derived values are stored with the notification and travel every hop; an app
/// that sets a field explicitly keeps its value.
/// </summary>
public static class PushKindDefaults
{
    /// <summary>A ring nobody answered within this is a missed call, not a late one.</summary>
    public const int RingTimeToLiveSeconds = 45;

    public static void Apply(AppNotificationOptions options)
    {
        switch (options.Kind)
        {
            case PushKind.Ring:
                options.TimeToLiveSeconds ??= RingTimeToLiveSeconds;
                // The same collapse id on the later "call ended" push retracts the ring.
                options.CollapseId ??= options.TagId == Guid.Empty ? null : $"call-{options.TagId:N}";
                options.TimeSensitive = true;
                options.Silent = false;
                break;

            case PushKind.Wake:
                options.Silent = true;
                break;
        }
    }
}
