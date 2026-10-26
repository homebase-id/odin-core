using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Notifications;

namespace Odin.Hosting.Tests._Universal.AppNotifications;

public class PushPreviewKeyRegistrationTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity> { TestIdentities.Samwise });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown() => _scaffold.RunAfterAnyTests();

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown() => _scaffold.AssertLogEvents();

    private static PushNotificationSubscribeFirebaseRequest Request(string previewKey) => new()
    {
        FriendlyName = "Sam's iPhone",
        DeviceToken = "fcm-token-123",
        DevicePlatform = "ios",
        PreviewKey = previewKey,
    };

    [Test]
    public async Task PreviewKey_IsStored_AndNeverEchoed()
    {
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        try
        {
            var subscribe = await ownerSam.AppNotifications.SubscribeFirebase(Request(key));
            Assert.That(subscribe.IsSuccessStatusCode, Is.True, $"subscribe failed: {subscribe.StatusCode}");

            var subscription = await ownerSam.AppNotifications.GetSubscription();
            Assert.That(subscription.Content!.HasPreviewKey, Is.True);
            Assert.That(typeof(Odin.Services.AppNotifications.Push.RedactedPushNotificationSubscription).GetProperty("PreviewKey"), Is.Null,
                "the key is a secret shared with the device; the API never returns it");
        }
        finally
        {
            await ownerSam.AppNotifications.UnsubscribeAll();
        }
    }

    [Test]
    public async Task WithoutPreviewKey_TheDeviceGetsNoPreviews()
    {
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        try
        {
            await ownerSam.AppNotifications.SubscribeFirebase(Request(""));
            var subscription = await ownerSam.AppNotifications.GetSubscription();
            Assert.That(subscription.Content!.HasPreviewKey, Is.False, "an older client never sends a key");
        }
        finally
        {
            await ownerSam.AppNotifications.UnsubscribeAll();
        }
    }

    [TestCase("c2hvcnQ=")]
    [TestCase("not base64!")]
    public async Task MalformedPreviewKey_IsRejected(string previewKey)
    {
        var ownerSam = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Samwise);
        try
        {
            var subscribe = await ownerSam.AppNotifications.SubscribeFirebase(Request(previewKey));
            Assert.That(subscribe.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        }
        finally
        {
            await ownerSam.AppNotifications.UnsubscribeAll();
        }
    }
}
