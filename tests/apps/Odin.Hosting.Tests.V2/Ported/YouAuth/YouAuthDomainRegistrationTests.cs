using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.YouAuth;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.YouAuth;
using Refit;

namespace Odin.Hosting.Tests.V2.Ported.YouAuth;

/// <summary>
/// Port of <c>OwnerApi/Membership/YouAuth/YouAuthDomainRegistrationTests</c>. What the owner console
/// can say about a third-party YouAuth domain: register it (plainly, with a consent expiry, with
/// circles), list and delete it, revoke and un-revoke it, grant and revoke circles on it, and manage
/// the clients registered under it.
/// </summary>
/// <remarks>
/// The YouAuth domain endpoints are the system under test here, not arrange, so every call goes
/// through the V1 Refit surface (<see cref="IRefitYouAuthDomainRegistration"/>) via
/// <see cref="OwnerSession.RefitFor{T}"/> rather than <c>owner.Admin.RegisterYouAuthDomain</c> --
/// the <c>Admin</c> helper fixes the consent requirements and throws on non-2xx, and several tests
/// here need to vary the first and read the second. Drives and circles remain arrange and stay on
/// <c>owner.Admin</c>.
/// <para>
/// No <c>SetupCallerWithOwner</c> is used (owner-only fixture, no caller matrix), so the
/// create-drive/build-caller ordering caveat does not apply.
/// </para>
/// <para>
/// <c>CanDeleteDomain</c> asserts an exact domain count of 2, which only holds because the per-test
/// DB restore puts the tenant back to a baseline with no YouAuth domains registered; that assertion
/// is carried over unchanged.
/// </para>
/// </remarks>
[TestFixture]
public class YouAuthDomainRegistrationTests : V2Fixture
{
    [Test]
    public async Task CanRegisterNewDomain()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain = new AsciiDomainName("amazoo4320m2.com");

        var expectedConsentRequirement = ConsentRequirementType.Never;
        var expectedConsentExpirationDateTime = UnixTimeUtc.ZeroTime;

        var response = await RegisterDomain(svc, domain, null, expectedConsentRequirement,
            expectedConsentExpirationDateTime);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var domainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
        Assert.That(domainRegistrationResponse.Content, Is.Not.Null);
        Assert.That(domainRegistrationResponse.Content.Domain, Is.EqualTo(domain.DomainName));

        Assert.That(domainRegistrationResponse.Content.ConsentRequirements.Expiration.milliseconds,
            Is.EqualTo(expectedConsentExpirationDateTime.milliseconds));
        Assert.That(domainRegistrationResponse.Content.ConsentRequirements.ConsentRequirementType,
            Is.EqualTo(expectedConsentRequirement));
    }

    [Test]
    public async Task CanRegisterNewDomainWithExpiringConsent()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain = new AsciiDomainName("ishallexpire.com");

        var expectedConsentRequirement = ConsentRequirementType.Expiring;
        var expectedConsentExpirationDateTime = UnixTimeUtc.Now().AddDays(10);

        var response = await RegisterDomain(svc, domain, null, expectedConsentRequirement,
            expectedConsentExpirationDateTime);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var domainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
        Assert.That(domainRegistrationResponse.Content, Is.Not.Null);
        Assert.That(domainRegistrationResponse.Content.Domain, Is.EqualTo(domain.DomainName));

        Assert.That(domainRegistrationResponse.Content.ConsentRequirements.Expiration.milliseconds,
            Is.EqualTo(expectedConsentExpirationDateTime.milliseconds));
        Assert.That(domainRegistrationResponse.Content.ConsentRequirements.ConsentRequirementType,
            Is.EqualTo(expectedConsentRequirement));
    }

    [Test]
    public async Task CanRegisterNewDomainWithCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain = new AsciiDomainName("amaz112coom2.com");

        var circle1 = await CreateCircle(owner, "Circle with valid permissions", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadConnections })
        });

        var someDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(someDrive, "Some drive",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        var circle2 = await CreateCircle(owner, "Circle with valid permissions", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = someDrive,
                        Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                    }
                }
            }
        });

        var response = await RegisterDomain(svc, domain, new List<GuidId> { circle1.Id, circle2.Id });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var domainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var domainRegistration = domainRegistrationResponse.Content;
        Assert.That(domainRegistration, Is.Not.Null);
        Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
        Assert.That(domainRegistration.IsRevoked, Is.False);
        Assert.That(domainRegistration.Created.milliseconds, Is.GreaterThan(0));

        var circle1Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle1.Id);
        Assert.That(circle1Grant, Is.Not.Null);
        Assert.That(circle1Grant.DriveGrants.Count, Is.EqualTo(circle1.DriveGrants?.Count() ?? 0));
        Assert.That(circle1Grant.PermissionSet.Keys.Count, Is.EqualTo(1));
        Assert.That(circle1Grant.PermissionSet.Keys, Is.EquivalentTo(circle1.Permissions.Keys));

        var circle2Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle2.Id);
        Assert.That(circle2Grant, Is.Not.Null);
        Assert.That(circle2Grant.DriveGrants.Count, Is.EqualTo(circle2.DriveGrants.Count()));
        Assert.That(circle2Grant.PermissionSet.Keys, Is.EquivalentTo(circle2.Permissions.Keys));
    }

    [Test]
    public async Task CanGetListOfDomains()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain1 = new AsciiDomainName("amazoomaa2333.com");
        var response1 = await RegisterDomain(svc, domain1);
        Assert.That(response1.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response1.StatusCode}");
        Assert.That(response1.Content, Is.Not.Null);

        var domain2 = new AsciiDomainName("bestbuyvi444us.com");
        var response2 = await RegisterDomain(svc, domain2);
        Assert.That(response2.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response2.StatusCode}");
        Assert.That(response2.Content, Is.Not.Null);

        var domainRegistrationResponse = await svc.GetRegisteredDomains();
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var results = domainRegistrationResponse.Content;
        Assert.That(results, Is.Not.Null);
        Assert.That(results, Has.Exactly(1).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domain1.DomainName));
        Assert.That(results, Has.Exactly(1).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domain2.DomainName));
    }

    [Test]
    public async Task CanDeleteDomain()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain1 = new AsciiDomainName("amazoomaa2.com");
        var response1 = await RegisterDomain(svc, domain1);
        Assert.That(response1.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response1.StatusCode}");
        Assert.That(response1.Content, Is.Not.Null);

        var domainToBeDeleted = new AsciiDomainName("aabestbuyvius.com");
        var response2 = await RegisterDomain(svc, domainToBeDeleted);
        Assert.That(response2.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response2.StatusCode}");
        Assert.That(response2.Content, Is.Not.Null);

        var domainRegistrationResponse = await svc.GetRegisteredDomains();
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var results = domainRegistrationResponse.Content;
        Assert.That(results, Is.Not.Null);
        Assert.That(results.Count, Is.EqualTo(2));

        Assert.That(results, Has.Exactly(1).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domain1.DomainName));
        Assert.That(results,
            Has.Exactly(1).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domainToBeDeleted.DomainName));

        //register a client for domain1
        var domain1ClientRegistrationResponse = await RegisterClient(svc, domain1, "some friendly name");
        Assert.That(domain1ClientRegistrationResponse.IsSuccessStatusCode, Is.True);

        //register a client for domainToBeDeleted
        var domainToBeDeletedClientRegistrationResponse = await RegisterClient(svc, domainToBeDeleted, "some friendly name");
        Assert.That(domainToBeDeletedClientRegistrationResponse.IsSuccessStatusCode, Is.True);

        // now delete domainToBeDeleted
        var deleteResponse = await svc.DeleteDomain(new GetYouAuthDomainRequest { Domain = domainToBeDeleted.DomainName });
        Assert.That(deleteResponse.IsSuccessStatusCode, Is.True);

        var updatedDomainList = await svc.GetRegisteredDomains();
        Assert.That(updatedDomainList.IsSuccessStatusCode, Is.True);

        var results2 = updatedDomainList.Content;
        Assert.That(results2, Is.Not.Null);
        Assert.That(results2.Count, Is.EqualTo(1));

        Assert.That(results2, Has.Exactly(1).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domain1.DomainName));
        Assert.That(results2, Has.Exactly(0).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domainToBeDeleted.DomainName),
            "domain2 should be deleted");

        //check that clients are gone

        var allClientsResponse = await svc.GetRegisteredClients(domainToBeDeleted.DomainName);
        Assert.That(allClientsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(allClientsResponse.Content, Is.Not.Null);

        Assert.That(allClientsResponse.Content, Is.Empty);
    }

    [Test]
    public async Task CanRevokeDomain_Then_RemoveRevocation()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain = new AsciiDomainName("amazoaac2om2.com");

        var response = await RegisterDomain(svc, domain);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var domainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
        var activeDomain = domainRegistrationResponse.Content;

        Assert.That(activeDomain, Is.Not.Null);
        Assert.That(activeDomain.Domain, Is.EqualTo(domain.DomainName));

        Assert.That(activeDomain.IsRevoked, Is.False);

        var revocationResponse = await svc.RevokeDomain(new GetYouAuthDomainRequest { Domain = domain.DomainName });
        Assert.That(revocationResponse.IsSuccessStatusCode, Is.True);

        var getRevokedDomainResponse = await GetDomainRegistration(svc, domain);
        Assert.That(getRevokedDomainResponse.IsSuccessStatusCode, Is.True);
        var revokedDomain = getRevokedDomainResponse.Content;
        Assert.That(revokedDomain, Is.Not.Null);
        Assert.That(revokedDomain.IsRevoked, Is.True);

        var allowDomainResponse =
            await svc.RemoveDomainRevocation(new GetYouAuthDomainRequest { Domain = domain.DomainName });
        Assert.That(allowDomainResponse.IsSuccessStatusCode, Is.True);

        var getAllowedDomainResponse = await GetDomainRegistration(svc, domain);
        Assert.That(getAllowedDomainResponse.IsSuccessStatusCode, Is.True);
        var allowedDomain = getAllowedDomainResponse.Content;
        Assert.That(allowedDomain, Is.Not.Null);
        Assert.That(allowedDomain.IsRevoked, Is.False);
    }

    [Test]
    public async Task ConnectedIdentitySystemCircleNotGrantedToYouAuthDomain()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain = new AsciiDomainName("amazoom333.com");

        var someDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(someDrive, "Some drive",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        var someCircle = await CreateCircle(owner, "Circle with valid permissions", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = someDrive,
                        Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                    }
                }
            }
        });

        var response = await RegisterDomain(svc, domain);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var domainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var domainRegistration = domainRegistrationResponse.Content;
        Assert.That(domainRegistration, Is.Not.Null);
        Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
        Assert.That(domainRegistration.IsRevoked, Is.False);
        Assert.That(domainRegistration.Created.milliseconds, Is.GreaterThan(0));

        //now grant the circle
        var grantCircleResponse = await svc.GrantCircle(new GrantYouAuthDomainCircleRequest
        {
            Domain = domain.DomainName,
            CircleId = someCircle.Id
        });
        Assert.That(grantCircleResponse.IsSuccessStatusCode, Is.True);

        var getUpdatedDomainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(getUpdatedDomainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var updatedDomainRegistrationResponse = await GetDomainRegistration(svc, domain);
        var updatedDomainRegistration = updatedDomainRegistrationResponse.Content;
        Assert.That(updatedDomainRegistration, Is.Not.Null);
        var someCircleGrant = updatedDomainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == someCircle.Id);
        Assert.That(someCircleGrant, Is.Not.Null);
        Assert.That(someCircleGrant.DriveGrants.Count, Is.EqualTo(someCircle.DriveGrants.Count()));
        Assert.That(someCircleGrant.PermissionSet.Keys, Is.EquivalentTo(someCircle.Permissions.Keys));

        // ensure the system circle was not granted
        Assert.That(
            updatedDomainRegistration.CircleGrants.SingleOrDefault(c =>
                c.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId),
            Is.Null,
            "The connected identities circle should not be granted to youauth domains");
    }

    [Test]
    public async Task CanGrantCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain = new AsciiDomainName("amazoom4422.com");

        var someDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(someDrive, "Some drive",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        var someCircle = await CreateCircle(owner, "Circle with valid permissions", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = someDrive,
                        Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                    }
                }
            }
        });

        var response = await RegisterDomain(svc, domain);

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var domainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var domainRegistration = domainRegistrationResponse.Content;
        Assert.That(domainRegistration, Is.Not.Null);
        Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
        Assert.That(domainRegistration.IsRevoked, Is.False);
        Assert.That(domainRegistration.Created.milliseconds, Is.GreaterThan(0));

        //now grant the circle
        var grantCircleResponse = await svc.GrantCircle(new GrantYouAuthDomainCircleRequest
        {
            Domain = domain.DomainName,
            CircleId = someCircle.Id
        });
        Assert.That(grantCircleResponse.IsSuccessStatusCode, Is.True);

        var getUpdatedDomainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(getUpdatedDomainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var updatedDomainRegistrationResponse = await GetDomainRegistration(svc, domain);
        var updatedDomainRegistration = updatedDomainRegistrationResponse.Content;
        Assert.That(updatedDomainRegistration, Is.Not.Null);
        var circle2Grant = updatedDomainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == someCircle.Id);
        Assert.That(circle2Grant, Is.Not.Null);
        Assert.That(circle2Grant.DriveGrants.Count, Is.EqualTo(someCircle.DriveGrants.Count()));
        Assert.That(circle2Grant.PermissionSet.Keys, Is.EquivalentTo(someCircle.Permissions.Keys));
    }

    [Test]
    public async Task CanRevokeCircle()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain = new AsciiDomainName("amazeen2.com");

        var circle1 = await CreateCircle(owner, "Circle with valid permissions", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadConnections })
        });

        var someDrive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(someDrive, "Some drive",
            allowAnonymousReads: false,
            allowSubscriptions: false,
            ownerOnly: false);

        var circle2 = await CreateCircle(owner, "Circle with valid permissions", new PermissionSetGrantRequest
        {
            PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = someDrive,
                        Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                    }
                }
            }
        });

        var response = await RegisterDomain(svc, domain, new List<GuidId> { circle1.Id, circle2.Id });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var domainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var domainRegistration = domainRegistrationResponse.Content;
        Assert.That(domainRegistration, Is.Not.Null);
        Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
        Assert.That(domainRegistration.IsRevoked, Is.False);
        Assert.That(domainRegistration.Created.milliseconds, Is.GreaterThan(0));

        var circle1Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle1.Id);
        Assert.That(circle1Grant, Is.Not.Null);
        Assert.That(circle1Grant.DriveGrants.Count, Is.EqualTo(circle1.DriveGrants?.Count() ?? 0));
        Assert.That(circle1Grant.PermissionSet.Keys.Count, Is.EqualTo(1));
        Assert.That(circle1Grant.PermissionSet.Keys, Is.EquivalentTo(circle1.Permissions.Keys));

        var circle2Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle2.Id);
        Assert.That(circle2Grant, Is.Not.Null);
        Assert.That(circle2Grant.DriveGrants.Count, Is.EqualTo(circle2.DriveGrants.Count()));
        Assert.That(circle2Grant.PermissionSet.Keys, Is.EquivalentTo(circle2.Permissions.Keys));

        // now revoke the circle 1

        var revokeCircle1Response = await svc.RevokeCircle(new RevokeYouAuthDomainCircleRequest
        {
            Domain = domain.DomainName,
            CircleId = circle1.Id
        });
        Assert.That(revokeCircle1Response.IsSuccessStatusCode, Is.True);

        var updatedDomainRegistrationResponse = await GetDomainRegistration(svc, domain);
        Assert.That(updatedDomainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var updatedDomainRegistration = updatedDomainRegistrationResponse.Content;
        Assert.That(updatedDomainRegistration, Is.Not.Null);

        Assert.That(updatedDomainRegistration.CircleGrants.Count, Is.EqualTo(1));
        Assert.That(updatedDomainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle1.Id), Is.Null);
    }

    [Test]
    public async Task CanRegisterClient()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain1 = new AsciiDomainName("amazoaap4om2.com");
        var response1 = await RegisterDomain(svc, domain1);
        Assert.That(response1.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response1.StatusCode}");
        Assert.That(response1.Content, Is.Not.Null);

        //register a client for domain1
        var domain1ClientRegistrationResponse = await RegisterClient(svc, domain1, "some friendly name");
        Assert.That(domain1ClientRegistrationResponse.IsSuccessStatusCode, Is.True);

        var allClientsResponse = await svc.GetRegisteredClients(domain1.DomainName);
        Assert.That(allClientsResponse.IsSuccessStatusCode, Is.True);
        var allClients = allClientsResponse.Content;
        Assert.That(allClients, Is.Not.Null);
        Assert.That(allClients,
            Has.Exactly(1).Matches<RedactedYouAuthDomainClient>(c => c.Domain.DomainName == domain1.DomainName));
    }

    [Test]
    public async Task CanGetListOfClients()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain1 = new AsciiDomainName("amazddoom2.com");
        var response1 = await RegisterDomain(svc, domain1);
        Assert.That(response1.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response1.StatusCode}");
        Assert.That(response1.Content, Is.Not.Null);

        var domain2 = new AsciiDomainName("bestddbuyvius.com");
        var response2 = await RegisterDomain(svc, domain2);
        Assert.That(response2.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response2.StatusCode}");
        Assert.That(response2.Content, Is.Not.Null);

        var domainRegistrationResponse = await svc.GetRegisteredDomains();
        Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

        var results = domainRegistrationResponse.Content;
        Assert.That(results, Is.Not.Null);
        Assert.That(results, Has.Exactly(1).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domain1.DomainName));
        Assert.That(results, Has.Exactly(1).Matches<RedactedYouAuthDomainRegistration>(d => d.Domain == domain2.DomainName));

        //register a client for domain1
        var domain1ClientRegistrationResponse = await RegisterClient(svc, domain1, "some friendly name");
        Assert.That(domain1ClientRegistrationResponse.IsSuccessStatusCode, Is.True);

        //register a client for domainToBeDeleted
        var domainToBeDeletedClientRegistrationResponse = await RegisterClient(svc, domain2, "some friendly name");
        Assert.That(domainToBeDeletedClientRegistrationResponse.IsSuccessStatusCode, Is.True);

        var domain1ClientsResponse = await svc.GetRegisteredClients(domain1.DomainName);
        Assert.That(domain1ClientsResponse.IsSuccessStatusCode, Is.True);
        var domain1Clients = domain1ClientsResponse.Content;
        Assert.That(domain1Clients.Count, Is.EqualTo(1));

        var domain2ClientsResponse = await svc.GetRegisteredClients(domain2.DomainName);
        Assert.That(domain2ClientsResponse.IsSuccessStatusCode, Is.True);
        var domain2Clients = domain2ClientsResponse.Content;
        Assert.That(domain2Clients.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task CanDeleteYouAuthClient()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitYouAuthDomainRegistration>();

        var domain1 = new AsciiDomainName("accmazoom2.com");
        var response1 = await RegisterDomain(svc, domain1);
        Assert.That(response1.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response1.StatusCode}");
        Assert.That(response1.Content, Is.Not.Null);

        //register a client for domain1
        var domain1ClientRegistrationResponse = await RegisterClient(svc, domain1, "some friendly name");
        Assert.That(domain1ClientRegistrationResponse.IsSuccessStatusCode, Is.True);

        var allClientsResponse = await svc.GetRegisteredClients(domain1.DomainName);
        Assert.That(allClientsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(allClientsResponse.Content, Is.Not.Null);
        Assert.That(allClientsResponse.Content,
            Has.Exactly(1).Matches<RedactedYouAuthDomainClient>(c => c.Domain.DomainName == domain1.DomainName));

        //delete the client
        var deleteClientResponse = await svc.DeleteClient(new GetYouAuthDomainClientRequest
        {
            AccessRegistrationId = domain1ClientRegistrationResponse.Content.AccessRegistrationId
        });
        Assert.That(deleteClientResponse.IsSuccessStatusCode, Is.True);

        var emptyClientsResponse = await svc.GetRegisteredClients(domain1.DomainName);
        Assert.That(emptyClientsResponse.IsSuccessStatusCode, Is.True);
        Assert.That(emptyClientsResponse.Content, Is.Not.Null);
        Assert.That(emptyClientsResponse.Content, Is.Empty);
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    /// <summary>
    /// Registers a YouAuth domain, mirroring the request the V1 <c>YouAuthDomainApiClient</c> built:
    /// a <c>Test_</c>-prefixed name, no CORS host, and whatever consent requirements the test names.
    /// </summary>
    private static Task<ApiResponse<RedactedYouAuthDomainRegistration>> RegisterDomain(
        IRefitYouAuthDomainRegistration svc,
        AsciiDomainName domain,
        List<GuidId> circleIds = null,
        ConsentRequirementType consentRequirement = ConsentRequirementType.Never,
        UnixTimeUtc consentExpiration = default)
    {
        return svc.RegisterDomain(new YouAuthDomainRegistrationRequest
        {
            Name = $"Test_{domain.DomainName}",
            Domain = domain.DomainName,
            CircleIds = circleIds ?? new List<GuidId>(),
            ConsentRequirements = new ConsentRequirements
            {
                ConsentRequirementType = consentRequirement,
                Expiration = consentExpiration
            }
        });
    }

    private static Task<ApiResponse<RedactedYouAuthDomainRegistration>> GetDomainRegistration(
        IRefitYouAuthDomainRegistration svc,
        AsciiDomainName domain)
    {
        return svc.GetRegisteredDomain(new GetYouAuthDomainRequest { Domain = domain.DomainName });
    }

    private static Task<ApiResponse<YouAuthDomainClientRegistrationResponse>> RegisterClient(
        IRefitYouAuthDomainRegistration svc,
        AsciiDomainName domain,
        string friendlyName)
    {
        return svc.RegisterClient(new YouAuthDomainClientRegistrationRequest
        {
            Domain = domain.DomainName,
            ClientFriendlyName = friendlyName
        });
    }

    /// <summary>
    /// Creates a circle and reads its definition back, which is what the V1
    /// <c>CircleMembershipApiClient.CreateCircle</c> returned — the tests here need the server's view
    /// of the drive grants and permission keys to compare the domain's circle grants against.
    /// </summary>
    private static async Task<CircleDefinition> CreateCircle(
        OwnerSession owner, string circleName, PermissionSetGrantRequest grant)
    {
        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, circleName, grant);
        var definition = await owner.Admin.GetCircleDefinition(circleId);
        Assert.That(definition.Content, Is.Not.Null);
        return definition.Content;
    }
}
