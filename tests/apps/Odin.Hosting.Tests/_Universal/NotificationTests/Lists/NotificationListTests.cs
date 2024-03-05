using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Peer;
using Odin.Core.Services.Peer.Outgoing;
using Odin.Core.Services.Peer.Outgoing.Drive;
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
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    public static IEnumerable TestCases()
    {
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.MethodNotAllowed };
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
        Assert.IsTrue(response1.IsSuccessStatusCode);

        var options2 = new AppNotificationOptions()
        {
            AppId = appId,
            TypeId = Guid.NewGuid(),
            TagId = Guid.NewGuid()
        };

        var response2 = await ownerApiClient.AppNotifications.AddNotification(options2);
        Assert.IsTrue(response2.IsSuccessStatusCode);

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());
        var response = await client.GetList(10);

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var results = response.Content?.Results;
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count == 2);
            Assert.IsNotNull(results.SingleOrDefault(d => d.Options.AppId == options1.AppId && d.Options.TypeId == options1.TypeId));
            Assert.IsNotNull(results.SingleOrDefault(d => d.Options.AppId == options2.AppId && d.Options.TypeId == options2.TypeId));
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

        Assert.IsTrue(response1.IsSuccessStatusCode);
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
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");


        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var getListResponse = await ownerApiClient.AppNotifications.GetList(1000);
            var results = getListResponse.Content.Results;
            Assert.IsNotNull(results);
            var notification = results.SingleOrDefault(n => n.Id == notificationId);
            Assert.IsNotNull(notification);
            Assert.IsTrue(notification.Unread == false);
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
        Assert.IsTrue(response1.IsSuccessStatusCode);
        var notificationId = response1.Content.NotificationId;

        // Act
        await callerContext.Initialize(ownerApiClient);
        var client = new AppNotificationsApiClient(identity.OdinId, callerContext.GetFactory());

        var response = await client.Delete(new List<Guid>() { notificationId });

        // Assert
        Assert.IsTrue(response.StatusCode == expectedStatusCode, $"Expected {expectedStatusCode} but actual was {response.StatusCode}");

        if (expectedStatusCode == HttpStatusCode.OK) //test more
        {
            var getListResponse = await ownerApiClient.AppNotifications.GetList(10000);
            var results = getListResponse.Content.Results;
            Assert.IsNotNull(results);
            Assert.IsTrue(results.All(n => n.Id != notificationId));
        }
    }
}