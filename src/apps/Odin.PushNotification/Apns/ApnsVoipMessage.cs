using System.Text.Json;
using Odin.Core.Dto;

namespace Odin.PushNotification.Apns;

/// <summary>
/// The HTTP/2 request for one VoIP push: path, headers and JSON body. Pure, so the shape is
/// testable without Apple. The body carries the same five keys the FCM data message carries
/// (<see cref="DevicePushNotificationRequestV1.ToClientDictionary"/>) at the top level, next to an
/// empty "aps", so the app parses one shape on both paths; "data" is the serialized
/// PushNotificationPayload and its options.tagId is the call id.
/// </summary>
public sealed record ApnsVoipMessage(string Path, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public static ApnsVoipMessage Build(DevicePushNotificationRequestV1 request, string bundleId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(request.VoipDeviceToken))
        {
            throw new ArgumentException("A VoIP push needs the device's PushKit token", nameof(request));
        }

        var headers = new Dictionary<string, string>
        {
            ["apns-topic"] = $"{bundleId}.voip",
            ["apns-push-type"] = "voip",
            ["apns-priority"] = "10",
            ["apns-id"] = request.Id,
        };

        if (request.TimeToLiveSeconds is > 0)
        {
            headers["apns-expiration"] = (now.ToUnixTimeSeconds() + request.TimeToLiveSeconds.Value).ToString();
        }

        if (!string.IsNullOrEmpty(request.CollapseId))
        {
            headers["apns-collapse-id"] = request.CollapseId;
        }

        var body = new Dictionary<string, object> { ["aps"] = new Dictionary<string, object>() };
        foreach (var (key, value) in request.ToClientDictionary())
        {
            body[key] = value;
        }

        return new ApnsVoipMessage($"/3/device/{request.VoipDeviceToken}", headers, JsonSerializer.Serialize(body));
    }
}
