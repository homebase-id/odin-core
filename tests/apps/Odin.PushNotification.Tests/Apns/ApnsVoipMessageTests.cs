using System.Text.Json;
using NUnit.Framework;
using Odin.Core.Dto;
using Odin.PushNotification.Apns;

namespace Odin.PushNotification.Tests.Apns;

public class ApnsVoipMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static DevicePushNotificationRequestV1 Ring() => new()
    {
        DevicePlatform = "ios",
        DeviceToken = "fcm-token",
        VoipDeviceToken = "voip-token-abc",
        OriginDomain = "frodo.dotyou.cloud",
        Signature = [1],
        Id = Guid.NewGuid().ToString(),
        Timestamp = Now.ToString("O"),
        CorrelationId = "corr",
        Data = "{\"options\":{\"tagId\":\"call-id\"}}",
        Title = "Homebase Chat",
        Body = "Sam is calling",
        FromDomain = "sam.dotyou.cloud",
        ToDomain = "frodo.dotyou.cloud",
        Kind = DevicePushNotificationRequestV1.Kinds.Ring,
        TimeToLiveSeconds = 45,
        CollapseId = "call-1",
    };

    [Test]
    public void Build_TargetsTheVoipTokenAndTopic_WithVoipHeaders()
    {
        var request = Ring();

        var message = ApnsVoipMessage.Build(request, "id.homebase.chat", Now);

        var headers = string.Join(", ", message.Headers.Select(h => $"{h.Key}={h.Value}"));
        Assert.That(message.Path, Is.EqualTo("/3/device/voip-token-abc"));
        Assert.That(message.Headers["apns-topic"], Is.EqualTo("id.homebase.chat.voip"), headers);
        Assert.That(message.Headers["apns-push-type"], Is.EqualTo("voip"), headers);
        Assert.That(message.Headers["apns-priority"], Is.EqualTo("10"), headers);
        Assert.That(message.Headers["apns-id"], Is.EqualTo(request.Id), headers);
        Assert.That(message.Headers["apns-expiration"], Is.EqualTo((Now.ToUnixTimeSeconds() + 45).ToString()), headers);
        Assert.That(message.Headers["apns-collapse-id"], Is.EqualTo("call-1"), headers);
    }

    [Test]
    public void Build_BodyCarriesTheSameKeysAsTheFcmDataMessage()
    {
        var request = Ring();

        var message = ApnsVoipMessage.Build(request, "id.homebase.chat", Now);

        using var body = JsonDocument.Parse(message.Body);
        var root = body.RootElement;
        Assert.That(root.TryGetProperty("aps", out _), Is.True, message.Body);
        foreach (var (key, value) in request.ToClientDictionary())
        {
            Assert.That(root.GetProperty(key).GetString(), Is.EqualTo(value), $"{key} in {message.Body}");
        }
    }

    [Test]
    public void Build_WithoutTtlOrCollapse_OmitsThoseHeaders()
    {
        var request = Ring();
        request.TimeToLiveSeconds = null;
        request.CollapseId = null;

        var message = ApnsVoipMessage.Build(request, "id.homebase.chat", Now);

        Assert.That(message.Headers.ContainsKey("apns-expiration"), Is.False);
        Assert.That(message.Headers.ContainsKey("apns-collapse-id"), Is.False);
    }

    [Test]
    public void Build_WithoutVoipToken_Throws()
    {
        var request = Ring();
        request.VoipDeviceToken = null;

        Assert.Throws<ArgumentException>(() => ApnsVoipMessage.Build(request, "id.homebase.chat", Now));
    }
}
