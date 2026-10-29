using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Util;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.DataSubscription.Follower;

namespace Odin.Hosting.Tests.V2.Ported.Follow;

/// <summary>
/// A signed-in visitor (YouAuth) on the v2 follower routes, as the public site uses them: the reads are open to
/// a guest, gated by the permissions <c>FollowerService</c> asserts, and the writes are not.
/// </summary>
[TestFixture]
public class FollowerGuestV2Tests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam, Identities.Merry];

    [Test]
    public async Task AGuestHoldingReadWhoIFollow_SeesWhoTheHostFollows()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);
        await sam.Followers.FollowAsync(frodo.Identity);

        var guest = await GuestSession.SetupAsync(sam, new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(PermissionKeys.ReadWhoIFollow)
        });
        var followers = new V2FollowerClient(guest.Identity, guest.Factory);

        var iFollow = await followers.GetIdentitiesIFollowAsync();
        Assert.That(iFollow.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(iFollow.Content!.Results, Does.Contain(frodo.Identity.DomainName));
    }

    [Test]
    public async Task AGuestWithoutReadWhoIFollow_IsRefused()
    {
        var sam = await LoginAsOwner(Identities.Sam);

        var guest = await GuestSession.SetupAsync(sam, new PermissionSetGrantRequest());
        var followers = new V2FollowerClient(guest.Identity, guest.Factory);

        var iFollow = await followers.GetIdentitiesIFollowAsync();
        Assert.That(iFollow.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
            "the route is open to guests, but the permission still has to be granted");
    }

    [Test]
    public async Task FollowerConfiguration_IsHowTheCallingGuestFollowsTheHost()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var merry = await LoginAsOwner(Identities.Merry);
        await merry.Followers.FollowAsync(sam.Identity);

        // Merry visits Sam's site; no permissions are needed to read her own follow
        var guest = await GuestSession.SetupAsync(sam, new PermissionSetGrantRequest(),
            new AsciiDomainName(Identities.Merry));
        var followers = new V2FollowerClient(guest.Identity, guest.Factory);

        var config = await followers.GetFollowerConfigurationAsync();
        Assert.That(config.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(config.Content!.OdinId, Is.EqualTo(merry.Identity));
        Assert.That(config.Content.NotificationType, Is.EqualTo(FollowerNotificationType.AllNotifications));
    }

    [Test]
    public async Task FollowerConfiguration_IsEmptyForAGuestWhoDoesNotFollow()
    {
        var sam = await LoginAsOwner(Identities.Sam);

        var guest = await GuestSession.SetupAsync(sam, new PermissionSetGrantRequest());
        var followers = new V2FollowerClient(guest.Identity, guest.Factory);

        var config = await followers.GetFollowerConfigurationAsync();
        Assert.That(config.IsSuccessStatusCode, Is.True, $"actual {config.StatusCode}");
        Assert.That(config.Content, Is.Null, "a visitor who does not follow has no follow to show");
    }

    [Test]
    public async Task AGuestCannotFollowOrUnfollowForTheHost()
    {
        var sam = await LoginAsOwner(Identities.Sam);
        var frodo = await LoginAsOwner(Identities.Frodo);

        var guest = await GuestSession.SetupAsync(sam, new PermissionSetGrantRequest());
        var followers = new V2FollowerClient(guest.Identity, guest.Factory);

        var follow = await followers.FollowAsync(new FollowRequest
        {
            OdinId = frodo.Identity,
            NotificationType = FollowerNotificationType.AllNotifications,
            Channels = []
        });
        Assert.That(follow.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));

        var unfollow = await followers.UnfollowAsync(new Odin.Hosting.Controllers.OdinIdRequest { OdinId = frodo.Identity });
        Assert.That(unfollow.StatusCode, Is.AnyOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden));
    }
}
