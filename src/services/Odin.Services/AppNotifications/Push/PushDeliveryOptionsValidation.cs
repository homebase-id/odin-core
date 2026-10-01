#nullable enable
using System;
using Odin.Core.Exceptions;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Services.AppNotifications.Push;

/// <summary>
/// Limits on the delivery options a client may put on a push. Applied where a client hands us
/// options directly (the push on a LiveRelay message); the file-transfer routes keep their own
/// validation.
/// </summary>
public static class PushDeliveryOptionsValidation
{
    /// <summary>A day. Longer than that and the push is not time-bound; leave TTL unset instead.</summary>
    public const int MaxTimeToLiveSeconds = 86400;

    /// <summary>The APNs limit for apns-collapse-id.</summary>
    public const int MaxCollapseIdLength = 64;

    public static void AssertValid(AppNotificationOptions options)
    {
        if (options.TypeId == Guid.Empty)
        {
            throw new OdinClientException("Push TypeId is required");
        }

        if (options.TimeToLiveSeconds is { } ttl && ttl is < 1 or > MaxTimeToLiveSeconds)
        {
            throw new OdinClientException($"Push TimeToLiveSeconds must be between 1 and {MaxTimeToLiveSeconds}, got {ttl}");
        }

        if (options.CollapseId is { Length: > MaxCollapseIdLength })
        {
            throw new OdinClientException($"Push CollapseId must be at most {MaxCollapseIdLength} characters, got {options.CollapseId.Length}");
        }
    }
}
