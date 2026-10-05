using Odin.Core.Dto;

namespace Odin.PushNotification.Tests;

/// <summary>One valid relay request for every test to start from; tests change the field or two they are about.</summary>
internal static class TestRequests
{
    public static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    public static DevicePushNotificationRequestV1 Request(string? kind = null, string? voipToken = null) => new()
    {
        DevicePlatform = "ios",
        DeviceToken = "device-token",
        VoipDeviceToken = voipToken,
        Kind = kind,
        OriginDomain = "frodo.dotyou.cloud",
        Signature = [1, 2, 3],
        Id = "message-id",
        Timestamp = Now.ToString("O"),
        CorrelationId = "corr",
        Data = "{\"payload\":true}",
        Title = "Homebase Chat",
        Body = "Sam is calling",
        FromDomain = "sam.dotyou.cloud",
        ToDomain = "frodo.dotyou.cloud",
    };
}
