#nullable enable
using Odin.Core.Dto;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Services.Util;

namespace Odin.Services.AppNotifications.Push;

/// <summary>
/// Limits on the delivery options of a push. The bounds are what the outbox and the push relay
/// accept, so they are checked once where every push is enqueued; the LiveRelay push additionally
/// requires a TypeId at its ingress.
/// </summary>
public static class PushDeliveryOptionsValidation
{
    public const int MaxTimeToLiveSeconds = DevicePushNotificationRequestV1.MaxTimeToLiveSeconds;
    public const int MaxCollapseIdLength = DevicePushNotificationRequestV1.MaxCollapseIdLength;

    /// <summary>A push handed over by a client or a peer: a type, plus the delivery bounds.</summary>
    public static void AssertValid(AppNotificationOptions options)
    {
        OdinValidationUtils.AssertNotEmptyGuid(options.TypeId, "Push TypeId");
        AssertDeliveryBounds(options);
    }

    /// <summary>The bounds every enqueued push must meet, whichever route it came in on.</summary>
    public static void AssertDeliveryBounds(AppNotificationOptions options)
    {
        if (options.TimeToLiveSeconds is { } ttl)
        {
            OdinValidationUtils.AssertIsTrue(ttl is >= 1 and <= MaxTimeToLiveSeconds,
                $"Push TimeToLiveSeconds must be between 1 and {MaxTimeToLiveSeconds}, got {ttl}");
        }

        if (options.CollapseId is { } collapseId)
        {
            OdinValidationUtils.AssertIsTrue(collapseId.Length <= MaxCollapseIdLength,
                $"Push CollapseId must be at most {MaxCollapseIdLength} characters, got {collapseId.Length}");
        }
    }
}
