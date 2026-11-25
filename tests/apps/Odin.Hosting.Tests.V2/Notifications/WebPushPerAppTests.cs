#nullable enable
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Notifications;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;

namespace Odin.Hosting.Tests.V2.Notifications;

/// <summary>
/// A web push subscription remembers the app that made it (#2006), so a push is sent to it with that app's payloads
/// only; see <c>PushNotificationService.WebPushContentFor</c>.
/// </summary>
[TestFixture]
public class WebPushPerAppTests : V2Fixture
{
    [Test]
    public async Task AnAppsWebPushSubscription_RemembersTheApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.EnsureDrive(drive, "Test Drive");
        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.Write, [PermissionKeys.SendPushNotifications]);

        var client = app.Factory.CreateHttpClient(app.Identity, out var sharedSecret);
        var push = RefitCreator.RestServiceFor<IRefitPushSubscription>(client, sharedSecret);
        try
        {
            var response = await push.Subscribe(new PushNotificationSubscribeDeviceRequest
            {
                FriendlyName = "journal", Endpoint = "https://push.example/endpoint", ExpirationTime = "",
                Auth = "auth", P256DH = "p256dh"
            });
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), response.Error?.Content);

            var (scope, context) = await MigrationContextAsync(owner);
            var service = scope.Resolve<PushNotificationService>();
            var subscription = (await service.GetAllSubscriptionsAsync(context)).Single(s => s.FriendlyName == "journal");
            Assert.That(subscription.AppId, Is.EqualTo(app.AppId));

            // As one made before the app was kept: found again from its app client's access registration, and stored
            subscription.AppId = null;
            await service.SaveSubscriptionAsync(subscription);
            var older = (await service.GetAllSubscriptionsAsync(context)).Single(s => s.FriendlyName == "journal");
            Assert.That(older.AppId, Is.Null, "precondition: stored as before the app was kept");
            await service.FillAppIdAsync(older);
            Assert.That(older.AppId, Is.EqualTo(app.AppId));
            Assert.That((await service.GetAllSubscriptionsAsync(context)).Single(s => s.FriendlyName == "journal").AppId,
                Is.EqualTo(app.AppId), "the app is kept, so the lookup isn't made again");

            // A key that is no app client (the owner console's) stays null: every app's pushes
            var ownerConsoles = new PushNotificationSubscription { AccessRegistrationId = Guid.NewGuid() };
            await service.FillAppIdAsync(ownerConsoles);
            Assert.That(ownerConsoles.AppId, Is.Null);
        }
        finally
        {
            // A subscription left behind would be pushed to by a later fixture, whose log check fails on the error.
            // Unsubscribe, not unsubscribeAll, which needs the master key
            Assert.That((await push.Unsubscribe()).StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }
    }
}
