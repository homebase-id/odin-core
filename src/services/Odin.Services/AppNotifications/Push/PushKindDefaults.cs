#nullable enable
using System;
using Odin.Core.Dto;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push;

/// <summary>
/// What a <see cref="PushKind"/> means in delivery terms. Applied once, where every push is
/// enqueued, so the derived values are stored with the notification and travel every hop. TTL and
/// collapse id are only filled in when the app left them unset; silent and time-sensitive are
/// fixed by the kind.
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
                options.CollapseId ??= CallCollapseId(options.TagId);
                options.TimeSensitive = true;
                options.Silent = false;
                break;

            case PushKind.Hangup:
                // Same collapse id and TTL as the ring it ends, so it replaces the ring on the
                // device and never arrives on its own after the ring would have expired.
                options.TimeToLiveSeconds ??= RingTimeToLiveSeconds;
                options.CollapseId ??= CallCollapseId(options.TagId);
                options.Silent = false;
                break;

            case PushKind.Wake:
                options.Silent = true;
                break;
        }
    }

    private static string? CallCollapseId(Guid callId) => callId == Guid.Empty ? null : $"call-{callId:N}";
}
