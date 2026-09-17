using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Util;
using Odin.Hosting.Controllers.Base.Membership.Connections;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.CircleMembership;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;

namespace Odin.Hosting.Tests.V2.Ported.Connections.CircleMembership;

/// <summary>
/// Port of <c>OwnerApi/Membership/CircleMembershipTests</c>. One circle holding two different kinds
/// of member — a connected identity and a YouAuth domain — is listed by the circle-membership
/// endpoint as two <c>CircleDomainResult</c> rows carrying the right <c>DomainType</c> and the
/// circle grant.
/// </summary>
/// <remarks>
/// The list endpoint is V1 only, so the call under test goes through
/// <see cref="ICircleMembershipOwnerHttpClient"/> via <see cref="OwnerSession.RefitFor{T}"/>.
/// Arrange (circle, YouAuth domain, connection) goes through <c>owner.Admin</c> /
/// <c>owner.Connections</c>.
/// <para>
/// The original made its circle with <c>client.Membership.CreateCircle</c>, which generates the
/// circle id itself; here the id is generated test-side. The only thing the test reads off the
/// circle is that id. <c>owner.Admin.CreateCircle</c> spells the description as
/// <c>"Description for {name}"</c> and <c>owner.Admin.RegisterYouAuthDomain</c> sets
/// <c>ConsentRequirementType.Never</c> — the original's default — neither of which any assertion
/// here reads.
/// </para>
/// <para>
/// <c>SetupCallerWithOwner</c> ordering is not in play: no caller matrix, <c>LoginAsOwner</c> only.
/// </para>
/// </remarks>
[TestFixture]
public class CircleMembershipTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task CanGetListOfDomainsByCircle()
    {
        var client = await LoginAsOwner(Identities.Frodo);
        var recipientClient = await LoginAsOwner(Identities.Sam);

        //
        // Create a circle
        //
        var circle1Id = Guid.NewGuid();
        await client.Admin.CreateCircle(circle1Id, "Circle with valid permissions", new PermissionSetGrantRequest()
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadConnections })
        });

        //
        // Add youauth domain
        //
        const string youAuthDomain = "amazoonius.org";
        await client.Admin.RegisterYouAuthDomain(new AsciiDomainName(youAuthDomain), new List<GuidId>() { circle1Id });

        //Add an identity
        var send = await client.Connections.SendConnectionRequest(recipientClient.Identity, new List<GuidId>() { circle1Id });
        Assert.That(send.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var accept = await recipientClient.Connections.AcceptConnectionRequest(client.Identity, new List<GuidId>());
        Assert.That(accept.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var getDomainsResponse = await client.RefitFor<ICircleMembershipOwnerHttpClient>()
            .GetDomainsInCircle(new GetCircleMembersRequest() { CircleId = circle1Id });
        var domains = getDomainsResponse.Content;
        Assert.That(domains, Is.Not.Null);
        Assert.That(domains.Count, Is.EqualTo(2));

        var identityRecord = domains.SingleOrDefault(d =>
            d.Domain.DomainName == recipientClient.Identity.DomainName && d.DomainType == DomainType.Identity);
        Assert.That(identityRecord, Is.Not.Null, "missing identity domain");
        Assert.That(identityRecord.CircleGrant, Is.Not.Null);
        Assert.That(identityRecord.CircleGrant.CircleId.Value, Is.EqualTo(circle1Id));
        Assert.That(identityRecord.CircleGrant.DriveGrants?.Count() ?? 0, Is.EqualTo(0));

        var youAuthDomainRecord = domains.SingleOrDefault(d =>
            d.Domain.DomainName == youAuthDomain && d.DomainType == DomainType.YouAuth);
        Assert.That(youAuthDomainRecord, Is.Not.Null, "missing youauth domain");
        Assert.That(youAuthDomainRecord.CircleGrant, Is.Not.Null);
        Assert.That(youAuthDomainRecord.CircleGrant.CircleId.Value, Is.EqualTo(circle1Id));
        Assert.That(youAuthDomainRecord.CircleGrant.DriveGrants?.Count() ?? 0, Is.EqualTo(0));
    }
}
