using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;

namespace Odin.Hosting.Tests.V2.Ported.Notifications;

/// <summary>
/// Port of <c>_Universal/NotificationTests/Lists/NotificationListTests</c>. Covers getting and
/// updating notifications after they have been created by an incoming push notification: listing
/// them, counting unread per app id, marking read (individually and by app id) and removing them —
/// each across the Owner / App / Guest caller matrix.
/// </summary>
/// <remarks>
/// These are V1 endpoints (<c>/notify/list...</c>) driven through the in-process host: the V1-shaped
/// <see cref="AppNotificationsApiClient"/> is reused unchanged, resolved against the fixture's
/// <c>Factory</c> via <c>InProcessApiClientFactory</c>'s V1 path normalization.
///
/// Most of the fixture is <c>[Ignore]</c>d upstream — notification mutations are no-ops pending a
/// refactor — and those attributes are carried over verbatim so the port stays a pure move.
/// </remarks>
[TestFixture]
public class NotificationListTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Sam];

    public static IEnumerable<object[]> TestCases()
    {
        yield return [CallerSpec.Owner(DriveSpec.Anon()), HttpStatusCode.OK];
        yield return [CallerSpec.Guest(DriveSpec.Anon(), DrivePermission.Write), HttpStatusCode.NotFound];
        yield return
        [
            CallerSpec.App(DriveSpec.Anon(), DrivePermission.Write, [PermissionKeys.SendPushNotifications]),
            HttpStatusCode.OK
        ];
    }

    [Test]
    [Ignore("Notification mutations (mark-read/delete) are disabled — service methods are no-ops pending refactor + upgrade. This test relies on Delete-based cleanup that no longer runs.")]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetListOfNotifications(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerNotifications = owner.V1.Notifications;

        var appId = Guid.NewGuid();
        var options1 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerNotifications.AddNotification(options1);
        Assert.That(response1.IsSuccessStatusCode, Is.True);

        var options2 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response2 = await ownerNotifications.AddNotification(options2);
        Assert.That(response2.IsSuccessStatusCode, Is.True);

        // Act
        var client = caller.V1.Notifications;
        var response = await client.GetList(10);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var results = response.Content?.Results;
            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.SingleOrDefault(d => d.Options.AppId == options1.AppId && d.Options.TypeId == options1.TypeId), Is.Not.Null);
            Assert.That(results.SingleOrDefault(d => d.Options.AppId == options2.AppId && d.Options.TypeId == options2.TypeId), Is.Not.Null);
        }

        await ownerNotifications.Delete([response1.Content.NotificationId, response2.Content.NotificationId]);
    }

    [Test]
    [Ignore("Notification mutations (mark-read/delete) are disabled — service methods are no-ops pending refactor + upgrade. This test relies on Delete-based cleanup that no longer runs.")]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetCountOfNotificationsPerAppId(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerNotifications = owner.V1.Notifications;

        var appId = Guid.NewGuid();
        var options1 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerNotifications.AddNotification(options1);
        Assert.That(response1.IsSuccessStatusCode, Is.True);

        var options2 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response2 = await ownerNotifications.AddNotification(options2);
        Assert.That(response2.IsSuccessStatusCode, Is.True);

        // Act
        var client = caller.V1.Notifications;
        var response = await client.GetList(10);


        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var results = response.Content?.Results;
            Assert.That(results, Is.Not.Null);
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.SingleOrDefault(d => d.Options.AppId == options1.AppId && d.Options.TypeId == options1.TypeId), Is.Not.Null);
            Assert.That(results.SingleOrDefault(d => d.Options.AppId == options2.AppId && d.Options.TypeId == options2.TypeId), Is.Not.Null);
        }

        await ownerNotifications.Delete([response1.Content.NotificationId, response2.Content.NotificationId]);
    }

    [Test]
    [Ignore("Notification mutations (mark-read/delete) are disabled — service methods are no-ops pending refactor + upgrade.")]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanMarkNotificationsRead(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerNotifications = owner.V1.Notifications;

        var options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerNotifications.AddNotification(options);

        Assert.That(response1.IsSuccessStatusCode, Is.True);
        var notificationId = response1.Content.NotificationId;

        // Act
        var client = caller.V1.Notifications;

        var updates = new List<UpdateNotificationRequest>()
        {
            new()
            {
                Id = notificationId,
                Unread = false
            }
        };

        var response = await client.Update(updates);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //test more

        var getListResponse = await ownerNotifications.GetList(1000);
        var results = getListResponse.Content.Results;
        Assert.That(results, Is.Not.Null);
        var notification = results.SingleOrDefault(n => n.Id == notificationId);
        Assert.That(notification, Is.Not.Null);
        Assert.That(notification.Unread, Is.False);
    }

    [Test]
    [Ignore("Notification mutations (mark-read/delete) are disabled — service methods are no-ops pending refactor + upgrade.")]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanMarkNotificationsReadByAppId(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerNotifications = owner.V1.Notifications;

        var appIdToBeMarkedAsRead = Guid.NewGuid();

        var options = new AppNotificationOptions()
        {
            AppId = appIdToBeMarkedAsRead,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var addNotificationResponse = await ownerNotifications.AddNotification(options);
        Assert.That(addNotificationResponse.IsSuccessStatusCode, Is.True);
        var notificationToBeMarkedAsRead = addNotificationResponse.Content.NotificationId;

        var notifyDiffAppResponse = await ownerNotifications.AddNotification(new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        });

        Assert.That(notifyDiffAppResponse.IsSuccessStatusCode, Is.True);
        var diffAppNotificationId = notifyDiffAppResponse.Content.NotificationId;

        // Act
        var client = caller.V1.Notifications;
        var response = await client.MarkReadByAppId(appIdToBeMarkedAsRead);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //test more

        var unreadCountsResponse2 = await ownerNotifications.GetUnreadCounts();
        Assert.That(unreadCountsResponse2.IsSuccessStatusCode, Is.True);
        Assert.That(unreadCountsResponse2.Content.UnreadCounts.TryGetValue(appIdToBeMarkedAsRead, out var counts), Is.True);
        Assert.That(counts, Is.EqualTo(0));

        var getListResponse = await ownerNotifications.GetList(1000);
        var results = getListResponse.Content.Results;
        Assert.That(results, Is.Not.Null);

        var notification1 = results.SingleOrDefault(n => n.Id == notificationToBeMarkedAsRead);
        Assert.That(notification1, Is.Not.Null);
        Assert.That(notification1.Unread, Is.False);

        var notificationDiffApp = results.SingleOrDefault(n => n.Id == diffAppNotificationId);
        Assert.That(notificationDiffApp, Is.Not.Null);
        Assert.That(notificationDiffApp.Unread, Is.True);
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetNotificationCountByAppId(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerNotifications = owner.V1.Notifications;

        var app1Options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response1 = await ownerNotifications.AddNotification(app1Options);
        Assert.That(response1.IsSuccessStatusCode, Is.True);

        var app2Options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response2 = await ownerNotifications.AddNotification(app2Options);

        Assert.That(response2.IsSuccessStatusCode, Is.True);

        // Act
        var client = caller.V1.Notifications;
        var response = await client.GetUnreadCounts();

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //test more

        var getCountsResponse = await ownerNotifications.GetUnreadCounts();
        var results = getCountsResponse.Content;
        Assert.That(results, Is.Not.Null);

        Assert.That(results.UnreadCounts[app1Options.AppId], Is.EqualTo(1));
        Assert.That(results.UnreadCounts[app2Options.AppId], Is.EqualTo(1));
    }

    [Test]
    [Ignore("Notification mutations (mark-read/delete) are disabled — service methods are no-ops pending refactor + upgrade.")]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanMarkNotificationsReadPerApp(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerNotifications = owner.V1.Notifications;

        var options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerNotifications.AddNotification(options);

        Assert.That(response1.IsSuccessStatusCode, Is.True);
        var notificationId = response1.Content.NotificationId;

        // Act
        var client = caller.V1.Notifications;

        var updates = new List<UpdateNotificationRequest>()
        {
            new()
            {
                Id = notificationId,
                Unread = false
            }
        };

        var response = await client.Update(updates);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //test more

        var getListResponse = await ownerNotifications.GetList(1000);
        var results = getListResponse.Content.Results;
        Assert.That(results, Is.Not.Null);
        var notification = results.SingleOrDefault(n => n.Id == notificationId);
        Assert.That(notification, Is.Not.Null);
        Assert.That(notification.Unread, Is.False);
    }

    [Test]
    [Ignore("Notification mutations (mark-read/delete) are disabled — service methods are no-ops pending refactor + upgrade.")]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanRemoveNotifications(CallerSpec spec, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var (caller, owner) = await SetupCallerWithOwner(spec);
        var ownerNotifications = owner.V1.Notifications;

        var options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerNotifications.AddNotification(options);
        Assert.That(response1.IsSuccessStatusCode, Is.True);
        var notificationId = response1.Content.NotificationId;

        // Act
        var client = caller.V1.Notifications;

        var response = await client.Delete(new List<Guid>() { notificationId });

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(expectedStatusCode));

        if (expectedStatusCode != HttpStatusCode.OK) return; //test more

        var getListResponse = await ownerNotifications.GetList(10000);
        var results = getListResponse.Content.Results;
        Assert.That(results, Is.Not.Null);
        Assert.That(results.All(n => n.Id != notificationId), Is.True);
    }
}
