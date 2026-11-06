using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.UnifiedV2;

namespace Odin.Hosting.Tests.V2.Ported.Notifications;

/// <summary>
/// The V2 notification list and the push-subscription list each answer at their own address. The
/// notification list used to sit under <c>/notify/push</c> too, where both controllers inherited a
/// <c>GET list</c> and neither could be reached.
/// </summary>
[TestFixture]
public class V2NotificationRoutesTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo];

    [Test]
    public async Task NotificationListAndPushSubscriptionListAreSeparateRoutes()
    {
        var owner = await LoginAsOwner();
        var (http, _) = owner.NewAdminHttpClient();

        var notifications = await http.GetAsync($"https://{Identities.Frodo}{UnifiedApiRouteConstants.NotifyRoot}/list?count=10");
        Assert.That(notifications.StatusCode, Is.EqualTo(HttpStatusCode.OK), "notification list");

        var subscriptions = await http.GetAsync($"https://{Identities.Frodo}{UnifiedApiRouteConstants.Notify}/list");
        Assert.That(subscriptions.StatusCode, Is.AnyOf(HttpStatusCode.OK, HttpStatusCode.NotFound),
            "push-subscription list (404 = no subscriptions, which is fine; 500 = ambiguous route)");
    }
}
