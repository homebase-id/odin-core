using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Hosting.Tests._Universal;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests._V2.ApiClient.TestCases;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests._V2.Tests.PushNotification;

public class PushNotificationVerifyTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Pippin });
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }

    public static IEnumerable OwnerAndAppTestCases()
    {
        yield return new object[] { new OwnerTestCase(TargetDrive.NewTargetDrive()) };
        yield return new object[]
        {
            new AppTestCase(
                TargetDrive.NewTargetDrive(),
                DrivePermission.Read,
                new TestPermissionKeyList(PermissionKeys.SendPushNotifications))
        };
    }

    [Test]
    [TestCaseSource(nameof(OwnerAndAppTestCases))]
    public async Task VerifyWithNoSubscription_ReturnsNoSubscription(IApiClientContext callerContext)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", false);
        await callerContext.Initialize(ownerApiClient);

        var client = new PushNotificationV2Client(identity.OdinId, callerContext.GetFactory());

        var response = await client.Verify();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(response.Content, Is.Not.Null);
        Assert.That(response.Content!.HasSubscription, Is.False);
        Assert.That(response.Content.SubscriptionType, Is.Null);
        Assert.That(response.Content.Subscription, Is.Null);

        await callerContext.Cleanup();
    }

    [Test]
    [TestCaseSource(nameof(OwnerAndAppTestCases))]
    public async Task SubscribeFirebaseThenVerify_ReturnsHasSubscription(IApiClientContext callerContext)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", false);
        await callerContext.Initialize(ownerApiClient);

        var client = new PushNotificationV2Client(identity.OdinId, callerContext.GetFactory());

        // Subscribe
        var subscribeResponse = await client.SubscribeFirebase(new PushNotificationSubscribeFirebaseRequest
        {
            FriendlyName = "Test Device",
            DeviceToken = "fake-firebase-token-12345",
            DevicePlatform = "Android"
        });
        Assert.That(subscribeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify
        var verifyResponse = await client.Verify();
        Assert.That(verifyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(verifyResponse.Content, Is.Not.Null);
        Assert.That(verifyResponse.Content!.HasSubscription, Is.True);
        Assert.That(verifyResponse.Content.SubscriptionType, Is.EqualTo("Firebase"));
        Assert.That(verifyResponse.Content.Subscription, Is.Not.Null);

        // Clean up
        await client.Unsubscribe();
        await callerContext.Cleanup();
    }

    [Test]
    [TestCaseSource(nameof(OwnerAndAppTestCases))]
    public async Task SubscribeFirebaseThenUnsubscribeThenVerify_ReturnsNoSubscription(IApiClientContext callerContext)
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", false);
        await callerContext.Initialize(ownerApiClient);

        var client = new PushNotificationV2Client(identity.OdinId, callerContext.GetFactory());

        // Subscribe
        var subscribeResponse = await client.SubscribeFirebase(new PushNotificationSubscribeFirebaseRequest
        {
            FriendlyName = "Test Device",
            DeviceToken = "fake-firebase-token-67890",
            DevicePlatform = "iOS"
        });
        Assert.That(subscribeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Unsubscribe
        var unsubscribeResponse = await client.Unsubscribe();
        Assert.That(unsubscribeResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        // Verify
        var verifyResponse = await client.Verify();
        Assert.That(verifyResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(verifyResponse.Content, Is.Not.Null);
        Assert.That(verifyResponse.Content!.HasSubscription, Is.False);

        await callerContext.Cleanup();
    }

    [Test]
    public async Task VerifyWithoutPermission_ReturnsForbidden()
    {
        var identity = TestIdentities.Pippin;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        // App with no SendPushNotifications permission
        var callerContext = new AppTestCase(
            TargetDrive.NewTargetDrive(),
            DrivePermission.Read);

        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", false);
        await callerContext.Initialize(ownerApiClient);

        var client = new PushNotificationV2Client(identity.OdinId, callerContext.GetFactory());

        var response = await client.Verify();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));

        await callerContext.Cleanup();
    }
}
