#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Refit;

namespace Odin.Hosting.Tests.V2.Notifications;

/// <summary>
/// A web push subscription remembers the app that made it (#2006), so a push is sent to it with that app's payloads
/// only; see <c>PushNotificationService.WebPushContentFor</c>.
/// </summary>
[TestFixture]
public class WebPushPerAppTests : V2Fixture
{
    public interface IRefitWebPushSubscribe
    {
        [Post("/notify/push/subscribe")]
        Task<ApiResponse<HttpContent>> Subscribe([Body] PushNotificationSubscribeDeviceRequest request);
    }

    [Test]
    public async Task AnAppsWebPushSubscription_RemembersTheApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.EnsureDrive(drive, "Test Drive");
        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.Write, [PermissionKeys.SendPushNotifications]);

        var client = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret);
        var response = await RefitCreator.RestServiceFor<IRefitWebPushSubscribe>(client, sharedSecret).Subscribe(
            new PushNotificationSubscribeDeviceRequest
            {
                FriendlyName = "journal", Endpoint = "https://push.example/endpoint", ExpirationTime = "",
                Auth = "auth", P256DH = "p256dh"
            });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

        var (scope, context) = await MigrationContextAsync(owner);
        var subscriptions = await scope.Resolve<PushNotificationService>().GetAllSubscriptionsAsync(context);
        Assert.That(subscriptions.Single(s => s.FriendlyName == "journal").AppId, Is.EqualTo(app.AppId));
    }
}
