using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.Notifications;

public interface IV2NotificationRoutes
{
    [Get(UnifiedApiRouteConstants.NotifyList + "/list")]
    Task<ApiResponse<HttpContent>> GetNotifications([Query] int count);

    [Get(UnifiedApiRouteConstants.Notify + "/list")]
    Task<ApiResponse<HttpContent>> GetPushSubscriptions();
}

/// <summary>
/// A person's notifications and a device's push subscriptions are two resources, each with its own "list".
/// They shared one URL, which answered every GET with a 500 before any controller ran.
/// </summary>
[TestFixture]
public class V2NotificationRouteTests : V2Fixture
{
    [Test]
    public async Task NotificationsAndPushSubscriptionsEachHaveTheirOwnList()
    {
        var owner = await LoginAsOwner();
        var routes = owner.RefitFor<IV2NotificationRoutes>();

        var notifications = await routes.GetNotifications(count: 10);
        Assert.That(notifications.StatusCode, Is.EqualTo(HttpStatusCode.OK), notifications.Error?.Content);

        var subscriptions = await routes.GetPushSubscriptions();
        Assert.That(subscriptions.StatusCode, Is.EqualTo(HttpStatusCode.OK), subscriptions.Error?.Content);
    }
}
