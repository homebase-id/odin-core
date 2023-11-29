using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.AppNotifications.Data;
using Odin.Core.Services.Drives;
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
        yield return new object[] { new GuestWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        yield return new object[] { new AppWriteOnlyAccessToDrive(TargetDrive.NewTargetDrive()), HttpStatusCode.Forbidden };
        yield return new object[] { new OwnerClientContext(TargetDrive.NewTargetDrive()), HttpStatusCode.OK };
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanGetListOfNotifications(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);
        var targetDrive = callerContext.TargetDrive;

        const string payload1 = "some payload1";
        var response1 = await ownerApiClient.AppNotifications.AddNotification(payload1);
        Assert.IsTrue(response1.IsSuccessStatusCode);

        const string payload2 = "some payload2";
        var response2 = await ownerApiClient.AppNotifications.AddNotification(payload2);
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
            Assert.IsNotNull(results.SingleOrDefault(d => d.Data == payload1));
            Assert.IsNotNull(results.SingleOrDefault(d => d.Data == payload2));
        }
    }

    [Test]
    [TestCaseSource(nameof(TestCases))]
    public async Task CanMarkNotificationsRead(IApiClientContext callerContext, HttpStatusCode expectedStatusCode)
    {
        // Setup
        var identity = TestIdentities.Samwise;
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(identity);

        const string payload1 = "some payload1";
        var response1 = await ownerApiClient.AppNotifications.AddNotification(payload1);
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
            var getListResponse = await ownerApiClient.AppNotifications.GetList(10);
            var results = getListResponse.Content.Results;
            Assert.IsNotNull(results);
            var notification = results.SingleOrDefault();
            Assert.IsNotNull(notification);
            Assert.IsTrue(notification.Id == notificationId);
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

        const string payload1 = "some payload1";
        var response1 = await ownerApiClient.AppNotifications.AddNotification(payload1);
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
            var getListResponse = await ownerApiClient.AppNotifications.GetList(10);
            var results = getListResponse.Content.Results;
            Assert.IsNotNull(results);
            var notification = results.SingleOrDefault();
            Assert.IsNull(notification);
        }
    }
}