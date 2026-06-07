using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.AppNotifications.Data;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Drives;
using Odin.Services.Peer.Outgoing.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Notifications;

namespace Odin.Hosting.Tests._Universal.NotificationTests.Lists;

// Covers getting and updating notifications after they have been created
// by an incoming push notification
public class NotificationListTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Samwise });
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


    public static IEnumerable TestCases()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.NotFound };
        yield return new object[]
            { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive(), new TestPermissionKeyList(PermissionKeys.SendPushNotifications)), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetListOfNotifications(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", true);

        var appId = Guid.NewGuid();
        var options1 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerApiClient.AppNotifications.AddNotification(options1);
        ClassicAssert.IsTrue(response1.IsSuccessStatusCode);

        var options2 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response2 = await ownerApiClient.AppNotifications.AddNotification(options2);
        ClassicAssert.IsTrue(response2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await client.GetList(10);

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var results = response.Content?.Results;
            ClassicAssert.IsNotNull(results);
            ClassicAssert.IsTrue(results.Count == 2);
            ClassicAssert.IsNotNull(results.SingleOrDefault(d => d.Options.AppId == options1.AppId && d.Options.TypeId == options1.TypeId));
            ClassicAssert.IsNotNull(results.SingleOrDefault(d => d.Options.AppId == options2.AppId && d.Options.TypeId == options2.TypeId));
        }

        await ownerApiClient.AppNotifications.Delete([response1.Content.NotificationId, response2.Content.NotificationId]);
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetCountOfNotificationsPerAppId(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;

        await ownerApiClient.DriveManager.CreateDrive(targetDrive, "Test Drive", "", true);

        var appId = Guid.NewGuid();
        var options1 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerApiClient.AppNotifications.AddNotification(options1);
        ClassicAssert.IsTrue(response1.IsSuccessStatusCode);

        var options2 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response2 = await ownerApiClient.AppNotifications.AddNotification(options2);
        ClassicAssert.IsTrue(response2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await client.GetList(10);


        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var results = response.Content?.Results;
            ClassicAssert.IsNotNull(results);
            ClassicAssert.IsTrue(results.Count == 2);
            ClassicAssert.IsNotNull(results.SingleOrDefault(d => d.Options.AppId == options1.AppId && d.Options.TypeId == options1.TypeId));
            ClassicAssert.IsNotNull(results.SingleOrDefault(d => d.Options.AppId == options2.AppId && d.Options.TypeId == options2.TypeId));
        }

        await ownerApiClient.AppNotifications.Delete([response1.Content.NotificationId, response2.Content.NotificationId]);
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanMarkNotificationsRead(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", true);

        var options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerApiClient.AppNotifications.AddNotification(options);

        ClassicAssert.IsTrue(response1.IsSuccessStatusCode);
        var notificationId = response1.Content.NotificationId;

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());

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
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");


        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var getListResponse = await ownerApiClient.AppNotifications.GetList(1000);
            var results = getListResponse.Content.Results;
            ClassicAssert.IsNotNull(results);
            var notification = results.SingleOrDefault(n => n.Id == notificationId);
            ClassicAssert.IsNotNull(notification);
            ClassicAssert.IsTrue(notification.Unread == false);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanMarkNotificationsReadByAppId(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", true);

        var appIdToBeMarkedAsRead = Guid.NewGuid();

        var options = new AppNotificationOptions()
        {
            AppId = appIdToBeMarkedAsRead,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var addNotificationResponse = await ownerApiClient.AppNotifications.AddNotification(options);
        ClassicAssert.IsTrue(addNotificationResponse.IsSuccessStatusCode);
        var notificationToBeMarkedAsRead = addNotificationResponse.Content.NotificationId;

        var notifyDiffAppResponse = await ownerApiClient.AppNotifications.AddNotification(new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        });

        ClassicAssert.IsTrue(notifyDiffAppResponse.IsSuccessStatusCode);
        var diffAppNotificationId = notifyDiffAppResponse.Content.NotificationId;

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await client.MarkReadByAppId(appIdToBeMarkedAsRead);

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var unreadCountsResponse2 = await ownerApiClient.AppNotifications.GetUnreadCounts();
            ClassicAssert.IsTrue(unreadCountsResponse2.IsSuccessStatusCode);
            ClassicAssert.IsTrue(unreadCountsResponse2.Content.UnreadCounts.TryGetValue(appIdToBeMarkedAsRead, out var counts));
            ClassicAssert.IsTrue(counts == 0);

            var getListResponse = await ownerApiClient.AppNotifications.GetList(1000);
            var results = getListResponse.Content.Results;
            ClassicAssert.IsNotNull(results);

            var notification1 = results.SingleOrDefault(n => n.Id == notificationToBeMarkedAsRead);
            ClassicAssert.IsNotNull(notification1);
            ClassicAssert.IsFalse(notification1.Unread);

            var notificationDiffApp = results.SingleOrDefault(n => n.Id == diffAppNotificationId);
            ClassicAssert.IsNotNull(notificationDiffApp);
            ClassicAssert.IsTrue(notificationDiffApp.Unread);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetNotificationCountByAppId(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", true);

        var app1Options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response1 = await ownerApiClient.AppNotifications.AddNotification(app1Options);
        ClassicAssert.IsTrue(response1.IsSuccessStatusCode);

        var app2Options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response2 = await ownerApiClient.AppNotifications.AddNotification(app2Options);

        ClassicAssert.IsTrue(response2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await client.GetUnreadCounts();

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var getCountsResponse = await ownerApiClient.AppNotifications.GetUnreadCounts();
            var results = getCountsResponse.Content;
            ClassicAssert.IsNotNull(results);

            ClassicAssert.IsTrue(results.UnreadCounts[app1Options.AppId] == 1);
            ClassicAssert.IsTrue(results.UnreadCounts[app2Options.AppId] == 1);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanMarkNotificationsReadPerApp(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", true);

        var options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerApiClient.AppNotifications.AddNotification(options);

        ClassicAssert.IsTrue(response1.IsSuccessStatusCode);
        var notificationId = response1.Content.NotificationId;

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());

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
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");


        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var getListResponse = await ownerApiClient.AppNotifications.GetList(1000);
            var results = getListResponse.Content.Results;
            ClassicAssert.IsNotNull(results);
            var notification = results.SingleOrDefault(n => n.Id == notificationId);
            ClassicAssert.IsNotNull(notification);
            ClassicAssert.IsTrue(notification.Unread == false);
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanRemoveNotifications(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", true);

        var options = new AppNotificationOptions()
        {
            AppId = Guid.NewGuid(),
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };
        var response1 = await ownerApiClient.AppNotifications.AddNotification(options);
        ClassicAssert.IsTrue(response1.IsSuccessStatusCode);
        var notificationId = response1.Content.NotificationId;

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());

        var response = await client.Delete(new List<Guid>() { notificationId });

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var getListResponse = await ownerApiClient.AppNotifications.GetList(10000);
            var results = getListResponse.Content.Results;
            ClassicAssert.IsNotNull(results);
            ClassicAssert.IsTrue(results.All(n => n.Id != notificationId));
        }
    }

    // Exercises a single Update request that both marks some notifications read and
    // flips another back to unread -- i.e. the two set-based UPDATE branches
    // (toRead / toUnread) and the multi-item IN (...) path.
    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanUpdateMultipleNotificationsWithMixedReadStates(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", true);

        var appId = Guid.NewGuid();
        AppNotificationOptions NewOptions() => new() { AppId = appId, TypeId = Guid.NewGuid(), TagId = Guid.NewGuid() };

        var r1 = await ownerApiClient.AppNotifications.AddNotification(NewOptions());
        var r2 = await ownerApiClient.AppNotifications.AddNotification(NewOptions());
        var r3 = await ownerApiClient.AppNotifications.AddNotification(NewOptions());
        ClassicAssert.IsTrue(r1.IsSuccessStatusCode && r2.IsSuccessStatusCode && r3.IsSuccessStatusCode);
        var id1 = r1.Content.NotificationId;
        var id2 = r2.Content.NotificationId;
        var id3 = r3.Content.NotificationId;

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());

        // Phase 1: mark all three read in one multi-item request (toRead branch, IN-list > 1)
        var markAllRead = new List<UpdateNotificationRequest>
        {
            new() { Id = id1, Unread = false },
            new() { Id = id2, Unread = false },
            new() { Id = id3, Unread = false },
        };
        var response = await client.Update(markAllRead);

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode != HttpStatusCode.OK)
        {
            return;
        }

        var afterPhase1 = (await ownerApiClient.AppNotifications.GetList(1000)).Content.Results;
        ClassicAssert.IsFalse(afterPhase1.Single(n => n.Id == id1).Unread);
        ClassicAssert.IsFalse(afterPhase1.Single(n => n.Id == id2).Unread);
        ClassicAssert.IsFalse(afterPhase1.Single(n => n.Id == id3).Unread);

        // Phase 2: one mixed request -- flip id1 back to unread (toUnread branch) and
        // keep id2 read (toRead branch); id3 is not in the request and must be untouched.
        var mixed = new List<UpdateNotificationRequest>
        {
            new() { Id = id1, Unread = true },
            new() { Id = id2, Unread = false },
        };
        var mixedResponse = await client.Update(mixed);
        ClassicAssert.IsTrue(mixedResponse.IsSuccessStatusCode);

        var afterPhase2 = (await ownerApiClient.AppNotifications.GetList(1000)).Content.Results;
        ClassicAssert.IsTrue(afterPhase2.Single(n => n.Id == id1).Unread, "id1 should have been flipped back to unread");
        ClassicAssert.IsFalse(afterPhase2.Single(n => n.Id == id2).Unread, "id2 should remain read");
        ClassicAssert.IsFalse(afterPhase2.Single(n => n.Id == id3).Unread, "id3 should be untouched");

        // Cleanup
        await ownerApiClient.AppNotifications.Delete([id1, id2, id3]);
    }

    // Exercises the bulk DELETE ... IN (...) path with more than one id in a single request.
    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanRemoveMultipleNotifications(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        await ownerApiClient.DriveManager.CreateDrive(callerContext.TargetDrive, "Test Drive", "", true);

        var appId = Guid.NewGuid();
        AppNotificationOptions NewOptions() => new() { AppId = appId, TypeId = Guid.NewGuid(), TagId = Guid.NewGuid() };

        var r1 = await ownerApiClient.AppNotifications.AddNotification(NewOptions());
        var r2 = await ownerApiClient.AppNotifications.AddNotification(NewOptions());
        var r3 = await ownerApiClient.AppNotifications.AddNotification(NewOptions());
        ClassicAssert.IsTrue(r1.IsSuccessStatusCode && r2.IsSuccessStatusCode && r3.IsSuccessStatusCode);
        var id1 = r1.Content.NotificationId;
        var id2 = r2.Content.NotificationId;
        var id3 = r3.Content.NotificationId;

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());

        // Delete id1 and id2 in one multi-item request; id3 stays.
        var response = await client.Delete([id1, id2]);

        // Assert
        ClassicAssert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK)
        {
            var results = (await ownerApiClient.AppNotifications.GetList(10000)).Content.Results;
            ClassicAssert.IsNotNull(results);
            ClassicAssert.IsTrue(results.All(n => n.Id != id1));
            ClassicAssert.IsTrue(results.All(n => n.Id != id2));
            ClassicAssert.IsNotNull(results.SingleOrDefault(n => n.Id == id3));

            // Cleanup
            await ownerApiClient.AppNotifications.Delete([id3]);
        }
    }
}