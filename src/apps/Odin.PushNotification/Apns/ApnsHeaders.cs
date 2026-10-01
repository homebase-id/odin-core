using Odin.Core.Dto;

namespace Odin.PushNotification.Apns;

/// <summary>The APNs headers every push type shares; the alert path (via Firebase) and the VoIP path (direct) both use it.</summary>
public static class ApnsHeaders
{
    public static void AddDeliveryHeaders(IDictionary<string, string> headers, DevicePushNotificationRequestV1 request, DateTimeOffset now)
    {
        if (request.TimeToLiveSeconds is > 0)
        {
            headers["apns-expiration"] = (now.ToUnixTimeSeconds() + request.TimeToLiveSeconds.Value).ToString();
        }

        if (!string.IsNullOrEmpty(request.CollapseId))
        {
            headers["apns-collapse-id"] = request.CollapseId;
        }
    }
}
