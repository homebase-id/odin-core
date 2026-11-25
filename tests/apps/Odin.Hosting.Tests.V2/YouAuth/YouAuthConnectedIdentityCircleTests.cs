using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;

namespace Odin.Hosting.Tests.V2.YouAuth;

/// <summary>
/// #2043: an identity is an identity.  When a YouAuth domain is a connected identity, its sign-in uses the
/// connection's circles, so its YouAuth registration holds no circle rows of its own, and a YouAuth grant can
/// neither take over the connection's row in a circle nor keep the connection out of one.
/// </summary>
[TestFixture]
public class YouAuthConnectedIdentityCircleTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task RegisteringAConnectedIdentityAsAYouAuthDomainLeavesItsCirclesAlone()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var circleId = await CreateCircleAsync(frodo);
        var otherCircleId = await CreateCircleAsync(frodo);

        await ConnectAsync(frodo, sam, circleId);

        // Registered, and saved again, it leaves the connection's rows alone
        var domain = new AsciiDomainName(sam.Identity.DomainName);
        await frodo.Admin.RegisterYouAuthDomain(domain, new List<GuidId> { circleId, otherCircleId });
        var svc = frodo.RefitFor<IRefitYouAuthDomainRegistration>();
        Assert.That((await svc.RevokeDomain(new GetYouAuthDomainRequest { Domain = domain.DomainName })).IsSuccessStatusCode, Is.True);
        Assert.That((await svc.RemoveDomainRevocation(new GetYouAuthDomainRequest { Domain = domain.DomainName })).IsSuccessStatusCode,
            Is.True);

        await AssertSamIsInCircleOnlyAsTheConnectionAsync(frodo, sam, circleId);
        Assert.That(await MembersAsync(frodo, otherCircleId), Is.Empty,
            "the YouAuth registration of a connected identity got a circle row of its own");
    }

    [Test]
    public async Task ConnectingAYouAuthDomainTakesItsCirclesFromTheConnection()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var circleId = await CreateCircleAsync(frodo);
        var otherCircleId = await CreateCircleAsync(frodo);

        await frodo.Admin.RegisterYouAuthDomain(new AsciiDomainName(sam.Identity.DomainName),
            new List<GuidId> { circleId, otherCircleId });
        Assert.That((await MembersAsync(frodo, circleId)).Single().DomainType, Is.EqualTo(DomainType.YouAuth),
            "arrange: the YouAuth domain is not in the circle");

        await ConnectAsync(frodo, sam, circleId);

        await AssertSamIsInCircleOnlyAsTheConnectionAsync(frodo, sam, circleId);
        Assert.That(await MembersAsync(frodo, otherCircleId), Is.Empty,
            "the YouAuth registration kept a circle row once the domain was a connection");
    }

    [Test]
    public async Task GrantingACircleToAConnectedIdentitysYouAuthDomainIsRefused()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        var circleId = await CreateCircleAsync(frodo);
        var otherCircleId = await CreateCircleAsync(frodo);

        await ConnectAsync(frodo, sam, circleId);
        await frodo.Admin.RegisterYouAuthDomain(new AsciiDomainName(sam.Identity.DomainName));

        var grant = await frodo.RefitFor<IRefitYouAuthDomainRegistration>()
            .GrantCircle(new GrantYouAuthDomainCircleRequest { Domain = sam.Identity.DomainName, CircleId = otherCircleId });

        Assert.That(grant.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "the grant was taken and dropped without a word");
        Assert.That(await MembersAsync(frodo, otherCircleId), Is.Empty);
    }

    private async Task AssertSamIsInCircleOnlyAsTheConnectionAsync(OwnerSession frodo, OwnerSession sam, Guid circleId)
    {
        var members = await MembersAsync(frodo, circleId);
        Assert.That(members.Select(m => (m.Domain.DomainName, m.DomainType)),
            Is.EqualTo(new[] { (sam.Identity.DomainName, DomainType.Identity) }),
            "the circle's row for the identity is not the connection's");

        var info = (await frodo.Connections.GetConnectionInfo(sam.Identity)).Content!;
        Assert.That(info.AccessGrant.CircleGrants.Any(c => c.CircleId == circleId), Is.True,
            "the connection lost the circle's grant");

        var registration = (await frodo.RefitFor<IRefitYouAuthDomainRegistration>()
            .GetRegisteredDomain(new GetYouAuthDomainRequest { Domain = sam.Identity.DomainName })).Content!;
        Assert.That(registration.CircleGrants, Is.Empty, "the YouAuth registration holds circle grants of its own");
    }

    private static async Task<Guid> CreateCircleAsync(OwnerSession owner)
    {
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, $"circle {circleId:N}", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadConnections })
        });
        return circleId;
    }

    /// <summary>Frodo sends, granting <paramref name="circleId"/>; Sam accepts.</summary>
    private static async Task ConnectAsync(OwnerSession frodo, OwnerSession sam, Guid circleId)
    {
        var send = await frodo.Connections.SendConnectionRequest(sam.Identity, new List<GuidId> { circleId });
        Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var accept = await sam.Connections.AcceptConnectionRequest(frodo.Identity, new List<GuidId>());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));
    }

    private static async Task<List<CircleDomainResult>> MembersAsync(OwnerSession owner, Guid circleId) =>
        (await owner.RefitFor<ICircleMembershipOwnerHttpClient>()
            .GetDomainsInCircle(new GetCircleMembersRequest { CircleId = circleId })).Content!;
}
