using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Odin.Core.Dto;
using Odin.PushNotification.Apns;

namespace Odin.PushNotification.Tests;

/// <summary>The one decision the relay makes: VoIP or Firebase, right before sending.</summary>
public class PushRouterTests
{
    private sealed class FakeFirebase : IPushNotification
    {
        public int Sent;
        public Task<string> Post(DevicePushNotificationRequestV1 request) { Sent++; return Task.FromResult("fcm"); }
    }

    private sealed class FakeApns(bool configured) : IApnsVoipSender
    {
        public int Sent;
        public bool IsConfigured => configured;
        public Task<string> SendVoipAsync(DevicePushNotificationRequestV1 request, CancellationToken ct = default) { Sent++; return Task.FromResult("apns"); }
    }

    [Test]
    public async Task Ring_WithVoipToken_AndApnsConfigured_GoesToApns()
    {
        var firebase = new FakeFirebase();
        var apns = new FakeApns(configured: true);
        var router = new PushRouter(firebase, apns, NullLogger<PushRouter>.Instance);

        var result = await router.SendAsync(TestRequests.Request(kind: "Ring", voipToken: "voip"));

        Assert.That(result, Is.EqualTo("apns"));
        Assert.That((apns.Sent, firebase.Sent), Is.EqualTo((1, 0)), "exactly one VoIP push and no Firebase push");
    }

    [Test]
    public async Task Ring_WithVoipToken_ButApnsNotConfigured_FallsBackToFirebase()
    {
        var firebase = new FakeFirebase();
        var apns = new FakeApns(configured: false);
        var router = new PushRouter(firebase, apns, NullLogger<PushRouter>.Instance);

        var result = await router.SendAsync(TestRequests.Request(kind: "Ring", voipToken: "voip"));

        Assert.That(result, Is.EqualTo("fcm"));
        Assert.That((apns.Sent, firebase.Sent), Is.EqualTo((0, 1)), "the alert push is the fallback until the Apple key exists");
    }

    [TestCase(null, "voip", Description = "no kind means Notify")]
    [TestCase("Notify", "voip")]
    [TestCase("Wake", "voip")]
    [TestCase("Hangup", "voip", Description = "only the ring itself is a VoIP push")]
    [TestCase("Ring", null, Description = "a ring to a device without a PushKit token")]
    [TestCase("Ring", "", Description = "an empty token is no token")]
    public async Task EverythingElse_GoesToFirebase(string? kind, string? voipToken)
    {
        var firebase = new FakeFirebase();
        var apns = new FakeApns(configured: true);
        var router = new PushRouter(firebase, apns, NullLogger<PushRouter>.Instance);

        var result = await router.SendAsync(TestRequests.Request(kind, voipToken));

        Assert.That(result, Is.EqualTo("fcm"));
        Assert.That((apns.Sent, firebase.Sent), Is.EqualTo((0, 1)), $"kind={kind ?? "null"} voip={voipToken ?? "null"}");
    }

    [Test]
    public async Task KindIsReadCaseInsensitively_AsTheHostWritesItCamelCased()
    {
        var firebase = new FakeFirebase();
        var apns = new FakeApns(configured: true);
        var router = new PushRouter(firebase, apns, NullLogger<PushRouter>.Instance);

        await router.SendAsync(TestRequests.Request(kind: "ring", voipToken: "voip"));

        Assert.That(apns.Sent, Is.EqualTo(1), "\"ring\" must mean Ring");
    }

    [Test]
    public void ApnsSender_WithoutConfiguration_StartsAndReportsNotConfigured()
    {
        using var sender = new ApnsVoipSender(new ApnsOptions(), NullLogger<ApnsVoipSender>.Instance);

        Assert.That(sender.IsConfigured, Is.False);
        Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendVoipAsync(TestRequests.Request("Ring", "voip")));
    }

    [Test]
    public void ApnsSender_ConfiguredButKeyFileMissing_RefusesToStart()
    {
        var options = new ApnsOptions { KeyId = "k", TeamId = "t", KeyFile = Path.Combine(Path.GetTempPath(), "no-such.p8"), BundleId = "b" };
        Assert.That(options.IsConfigured, Is.True, "all four values are set");

        Assert.Throws<FileNotFoundException>(() => new ApnsVoipSender(options, NullLogger<ApnsVoipSender>.Instance),
            "a deploy that names a key it does not ship must fail loudly, like the Firebase key");
    }

    [Test]
    public void ApnsOptions_HostFollowsTheEnvironment()
    {
        var options = new ApnsOptions();
        Assert.That(options.Host, Is.EqualTo(new Uri("https://api.sandbox.push.apple.com")), "sandbox is the default");

        options.Environment = "production";
        Assert.That(options.Host, Is.EqualTo(new Uri("https://api.push.apple.com")));
    }
}
