using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Odin.Core.Dto;

namespace Odin.PushNotification.Apns;

/// <summary>
/// The HTTP/2 request for one VoIP push: path, headers and JSON body, and the HttpRequestMessage
/// built from them. Free of I/O, so the shape is testable without Apple. The body carries the same five keys as the FCM data message
/// (<see cref="DevicePushNotificationRequestV1.ToClientDictionary"/>) next to an empty "aps", so
/// the app parses one shape on both paths.
/// </summary>
public sealed record ApnsVoipMessage(string Path, IReadOnlyDictionary<string, string> Headers, string Body)
{
    public static ApnsVoipMessage Build(DevicePushNotificationRequestV1 request, string bundleId, DateTimeOffset now)
    {
        var headers = new Dictionary<string, string>
        {
            ["apns-topic"] = $"{bundleId}.voip",
            ["apns-push-type"] = "voip",
            ["apns-priority"] = "10",
            ["apns-id"] = request.Id,
        };
        ApnsHeaders.AddDeliveryHeaders(headers, request, now);

        var body = new Dictionary<string, object> { ["aps"] = new Dictionary<string, object>() };
        foreach (var (key, value) in request.ToClientDictionary())
        {
            body[key] = value;
        }

        return new ApnsVoipMessage($"/3/device/{request.VoipDeviceToken}", headers, JsonSerializer.Serialize(body));
    }

    public HttpRequestMessage ToHttpRequest(string providerToken)
    {
        // A request's own version wins over HttpClient.DefaultRequestVersion, and APNs only speaks HTTP/2.
        var http = new HttpRequestMessage(HttpMethod.Post, Path)
        {
            Version = HttpVersion.Version20,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact,
            Content = new StringContent(Body, Encoding.UTF8, "application/json"),
        };
        http.Headers.Authorization = new AuthenticationHeaderValue("bearer", providerToken);
        foreach (var (name, value) in Headers)
        {
            http.Headers.TryAddWithoutValidation(name, value);
        }
        return http;
    }
}
