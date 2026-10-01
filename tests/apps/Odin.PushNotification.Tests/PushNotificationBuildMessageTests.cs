using FirebaseAdmin.Messaging;
using NUnit.Framework;
using Odin.Core.Dto;

namespace Odin.PushNotification.Tests;

/// <summary>
/// The FCM message the relay builds from a host request. BuildMessage is pure, so no Firebase
/// credentials are needed. The host side of the same contract is PushNotificationService.DevicePushAsync.
/// </summary>
public class PushNotificationBuildMessageTests
{
    private static readonly DateTimeOffset Now = TestRequests.Now;

    private static DevicePushNotificationRequestV1 Request() => TestRequests.Request();

    private static string Describe(Message message) =>
        $"apns headers: {string.Join(", ", message.Apns.Headers.Select(h => $"{h.Key}={h.Value}"))}; " +
        $"alert: {(message.Apns.Aps.Alert == null ? "none" : message.Apns.Aps.Alert.Title)}; " +
        $"android ttl: {message.Android.TimeToLive?.ToString() ?? "none"}, collapse: {message.Android.CollapseKey ?? "none"}";

    [Test]
    public void Default_IsAnAlertPushAtPriority10_WithNoExpiryOrCollapse()
    {
        var message = PushNotification.BuildMessage(Request(), Now);

        Assert.That(message.Token, Is.EqualTo("device-token"));
        Assert.That(message.Android.Priority, Is.EqualTo(Priority.High));
        Assert.That(message.Android.TimeToLive, Is.Null, Describe(message));
        Assert.That(message.Android.CollapseKey, Is.Null, Describe(message));
        Assert.That(message.Apns.Headers["apns-push-type"], Is.EqualTo("alert"), Describe(message));
        Assert.That(message.Apns.Headers["apns-priority"], Is.EqualTo("10"), Describe(message));
        Assert.That(message.Apns.Headers.ContainsKey("apns-expiration"), Is.False, Describe(message));
        Assert.That(message.Apns.Headers.ContainsKey("apns-collapse-id"), Is.False, Describe(message));
        Assert.That(message.Apns.Aps.Alert, Is.Not.Null, Describe(message));
        Assert.That(message.Apns.Aps.Alert.Title, Is.EqualTo("Homebase Chat"));
        Assert.That(message.Apns.Aps.Alert.Body, Is.EqualTo("Sam is calling"));
        Assert.That(message.Apns.Aps.ContentAvailable, Is.True);
        Assert.That(message.Apns.Aps.MutableContent, Is.True);
        Assert.That(message.Apns.Aps.CustomData, Is.Null, Describe(message));
    }

    [Test]
    public void Silent_IsABackgroundPushAtPriority5_WithoutAlert()
    {
        var request = Request();
        request.Silent = true;

        var message = PushNotification.BuildMessage(request, Now);

        Assert.That(message.Apns.Headers["apns-push-type"], Is.EqualTo("background"), Describe(message));
        Assert.That(message.Apns.Headers["apns-priority"], Is.EqualTo("5"), Describe(message));
        Assert.That(message.Apns.Aps.Alert, Is.Null, Describe(message));
        Assert.That(message.Apns.Aps.ContentAvailable, Is.True, "a background push must say content-available");
        // Android is data-only in both cases; the app decides what to show.
        Assert.That(message.Android.Priority, Is.EqualTo(Priority.High));
        Assert.That(message.Notification, Is.Null);
    }

    [Test]
    public void TimeToLive_SetsAndroidTtlAndApnsExpiration()
    {
        var request = Request();
        request.TimeToLiveSeconds = 45;

        var message = PushNotification.BuildMessage(request, Now);

        Assert.That(message.Android.TimeToLive, Is.EqualTo(TimeSpan.FromSeconds(45)), Describe(message));
        Assert.That(message.Apns.Headers["apns-expiration"], Is.EqualTo((Now.ToUnixTimeSeconds() + 45).ToString()), Describe(message));
    }

    [TestCase(null)]
    [TestCase(0)]
    public void NoTimeToLive_SetsNeither(int? ttl)
    {
        var request = Request();
        request.TimeToLiveSeconds = ttl;

        var message = PushNotification.BuildMessage(request, Now);

        Assert.That(message.Android.TimeToLive, Is.Null, Describe(message));
        Assert.That(message.Apns.Headers.ContainsKey("apns-expiration"), Is.False, Describe(message));
    }

    [Test]
    public void CollapseId_SetsAndroidCollapseKeyAndApnsCollapseId()
    {
        var request = Request();
        request.CollapseId = "call-7f3a";

        var message = PushNotification.BuildMessage(request, Now);

        Assert.That(message.Android.CollapseKey, Is.EqualTo("call-7f3a"), Describe(message));
        Assert.That(message.Apns.Headers["apns-collapse-id"], Is.EqualTo("call-7f3a"), Describe(message));
    }

    [Test]
    public void TimeSensitive_SetsTheInterruptionLevel()
    {
        var request = Request();
        request.TimeSensitive = true;

        var message = PushNotification.BuildMessage(request, Now);

        Assert.That(message.Apns.Aps.CustomData, Is.Not.Null, Describe(message));
        Assert.That(message.Apns.Aps.CustomData["interruption-level"], Is.EqualTo("time-sensitive"));
    }

    [Test]
    public void Data_StillCarriesTheClientDictionary()
    {
        var request = Request();
        request.TimeToLiveSeconds = 30;
        request.CollapseId = "x";

        var message = PushNotification.BuildMessage(request, Now);

        Assert.That(message.Data, Is.EqualTo(request.ToClientDictionary()),
            "delivery options must not leak into the data the app reads");
        Assert.That(message.Data.Keys, Is.EquivalentTo(new[] { "correlationId", "id", "data", "timestamp", "version" }));
    }
}
