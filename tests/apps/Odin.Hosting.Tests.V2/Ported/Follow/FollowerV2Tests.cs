using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;

namespace Odin.Hosting.Tests.V2.Ported.Follow;

/// <summary>
/// Covers the <c>/api/v2/followers/*</c> surface added for
/// <see href="https://github.com/homebase-id/odin-core/issues/1611">#1611</see>, where every one of
/// these routes 404'd because no follower controller existed under <c>UnifiedV2/</c>.
///
/// The follow *logic* is shared with the v1 owner/app/guest controllers via
/// <c>FollowerControllerBase</c> and is covered by their tests; what these assert is that the v2
/// routes exist, bind, and run under the v2 bearer-token policy. The 404 regression is asserted
/// explicitly in <see cref="AllFollowerRoutes_AreRouted_AndDoNotReturnNotFound"/>.
/// </summary>
[TestFixture]
public class FollowerV2Tests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task Follow_ThenIdentitiesIFollow_ReturnsTheFollowedIdentity()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var follow = await frodo.Followers.FollowAsync(sam.Identity);
        Assert.That(follow.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var iFollow = await frodo.Followers.GetIdentitiesIFollowAsync();
        Assert.That(iFollow.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(iFollow.Content!.Results, Does.Contain(sam.Identity.DomainName));
    }

    [Test]
    public async Task Follow_ThenFollowingMe_OnTheRecipient_ReturnsTheFollower()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Followers.FollowAsync(sam.Identity);

        // The follow is delivered to Sam over the perimeter, so Sam is the one who can see it.
        var followingSam = await sam.Followers.GetFollowingMeAsync();
        Assert.That(followingSam.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(followingSam.Content!.Results, Does.Contain(frodo.Identity.DomainName));
    }

    [Test]
    public async Task GetIdentityIFollow_ReturnsTheDefinition()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Followers.FollowAsync(sam.Identity);

        var definition = await frodo.Followers.GetIdentityIFollowAsync(sam.Identity);
        Assert.That(definition.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(definition.Content!.OdinId, Is.EqualTo(sam.Identity));
    }

    [Test]
    public async Task GetFollower_OnTheRecipient_ReturnsTheDefinition()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Followers.FollowAsync(sam.Identity);

        var definition = await sam.Followers.GetFollowerAsync(frodo.Identity);
        Assert.That(definition.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(definition.Content!.OdinId, Is.EqualTo(frodo.Identity));
    }

    [Test]
    public async Task Unfollow_RemovesTheIdentityFromIdentitiesIFollow()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Followers.FollowAsync(sam.Identity);

        var unfollow = await frodo.Followers.UnfollowAsync(sam.Identity);
        Assert.That(unfollow.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));

        var iFollow = await frodo.Followers.GetIdentitiesIFollowAsync();
        Assert.That(iFollow.Content!.Results, Does.Not.Contain(sam.Identity.DomainName));

        var followingSam = await sam.Followers.GetFollowingMeAsync();
        Assert.That(followingSam.Content!.Results, Does.Not.Contain(frodo.Identity.DomainName));
    }

    [Test]
    public async Task SynchronizeFeedHistory_Succeeds()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        await frodo.Followers.FollowAsync(sam.Identity);

        var sync = await frodo.Followers.SynchronizeFeedHistoryAsync(sam.Identity);
        Assert.That(sync.IsSuccessStatusCode, Is.True, $"sync-feed-history returned {sync.StatusCode}");
    }

    /// <summary>
    /// The regression itself: before the v2 controller existed, every one of these returned 404 and
    /// the KMP feed client rendered "You're not following anyone yet." Asserts routing only — the
    /// status just has to be something other than NotFound.
    /// </summary>
    [Test]
    public async Task AllFollowerRoutes_AreRouted_AndDoNotReturnNotFound()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var statuses = new[]
        {
            (route: "POST /follow", status: (await frodo.Followers.FollowAsync(sam.Identity)).StatusCode),
            (route: "GET /IdentitiesIFollow", status: (await frodo.Followers.GetIdentitiesIFollowAsync()).StatusCode),
            (route: "GET /followingme", status: (await frodo.Followers.GetFollowingMeAsync()).StatusCode),
            (route: "GET /IdentityIFollow", status: (await frodo.Followers.GetIdentityIFollowAsync(sam.Identity)).StatusCode),
            (route: "GET /follower", status: (await sam.Followers.GetFollowerAsync(frodo.Identity)).StatusCode),
            (route: "POST /sync-feed-history", status: (await frodo.Followers.SynchronizeFeedHistoryAsync(sam.Identity)).StatusCode),
            (route: "POST /unfollow", status: (await frodo.Followers.UnfollowAsync(sam.Identity)).StatusCode)
        };

        Assert.Multiple(() =>
        {
            foreach (var (route, status) in statuses)
            {
                Assert.That(status, Is.Not.EqualTo(HttpStatusCode.NotFound), $"{route} is not routed on /api/v2/followers");
            }
        });

        Assert.That(statuses.All(s => (int)s.status < 400), Is.True,
            "expected every follower route to succeed: " + string.Join(", ", statuses.Select(s => $"{s.route}={s.status}")));
    }
}
