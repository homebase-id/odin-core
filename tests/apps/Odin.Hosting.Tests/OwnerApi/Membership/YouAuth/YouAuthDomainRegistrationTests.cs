using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Core.Time;
using Odin.Core.Util;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Membership.YouAuth
{
    public class YouAuthDomainRegistrationTests
    {
        private WebScaffold _scaffold;

        private readonly TestIdentity _identity = TestIdentities.Frodo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var folder = GetType().Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
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


        [Test]
        public async Task CanRegisterNewDomain()
        {
            var domain = new AsciiDomainName("amazoo4320m2.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var expectedConsentRequirement = ConsentRequirementType.Never;
            var expectedConsentExpirationDateTime = UnixTimeUtc.ZeroTime;

            var response = await client.YouAuth.RegisterDomain(domain, null, expectedConsentRequirement, expectedConsentExpirationDateTime);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
            Assert.That(domainRegistrationResponse.Content, Is.Not.Null);
            Assert.That(domainRegistrationResponse.Content.Domain, Is.EqualTo(domain.DomainName));

            Assert.That(domainRegistrationResponse.Content.ConsentRequirements.Expiration == expectedConsentExpirationDateTime);
            Assert.That(domainRegistrationResponse.Content.ConsentRequirements.ConsentRequirementType == expectedConsentRequirement);
        }

        [Test]
        public async Task CanRegisterNewDomainWithExpiringConsent()
        {
            var domain = new AsciiDomainName("ishallexpire.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var expectedConsentRequirement = ConsentRequirementType.Expiring;
            var expectedConsentExpirationDateTime = new UnixTimeUtc(DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeMilliseconds());

            var response = await client.YouAuth.RegisterDomain(domain, null, expectedConsentRequirement, expectedConsentExpirationDateTime);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
            Assert.That(domainRegistrationResponse.Content, Is.Not.Null);
            Assert.That(domainRegistrationResponse.Content.Domain, Is.EqualTo(domain.DomainName));

            Assert.That(domainRegistrationResponse.Content.ConsentRequirements.Expiration == expectedConsentExpirationDateTime);
            Assert.That(domainRegistrationResponse.Content.ConsentRequirements.ConsentRequirementType == expectedConsentRequirement);
        }

        [Test]
        public async Task CanRegisterNewDomainWithCircle()
        {
            var domain = new AsciiDomainName("amaz112coom2.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var circle1 = await client.Membership.CreateCircle("Circle with valid permissions", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadConnections })
            });

            var someDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var circle2 = await client.Membership.CreateCircle("Circle with valid permissions", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = someDrive.TargetDriveInfo,
                            Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                        }
                    }
                }
            });

            var response = await client.YouAuth.RegisterDomain(domain, new List<GuidId>() { circle1.Id, circle2.Id });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var domainRegistration = domainRegistrationResponse.Content;
            Assert.That(domainRegistration, Is.Not.Null);
            Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
            Assert.IsFalse(domainRegistration.IsRevoked);
            Assert.IsTrue(domainRegistration.Created > 0);
            // Assert.IsNotNull(domainRegistration.CorsHostName);

            var circle1Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle1.Id);
            Assert.IsNotNull(circle1Grant);
            Assert.IsTrue(circle1Grant.DriveGrants.Count == (circle1.DriveGrants?.Count() ?? 0));
            Assert.IsTrue(circle1Grant.PermissionSet.Keys.Count == 1);
            CollectionAssert.AreEquivalent(circle1Grant.PermissionSet.Keys, circle1.Permissions.Keys);

            var circle2Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle2.Id);
            Assert.IsNotNull(circle2Grant);
            Assert.IsTrue(circle2Grant.DriveGrants.Count == circle2.DriveGrants.Count());
            CollectionAssert.AreEquivalent(circle2Grant.PermissionSet.Keys, circle2.Permissions.Keys);
        }

        [Test]
        public async Task CanGetListOfDomains()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazoomaa2333.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            var domain2 = new AsciiDomainName("bestbuyvi444us.com");
            var response2 = await client.YouAuth.RegisterDomain(domain2);
            Assert.IsTrue(response2.IsSuccessStatusCode, $"Failed status code.  Value was {response2.StatusCode}");
            Assert.IsNotNull(response2.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomains();
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var results = domainRegistrationResponse.Content;
            Assert.IsNotNull(results);
            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain1.DomainName));
            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain2.DomainName));
        }

        [Test]
        public async Task CanDeleteDomain()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazoomaa2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            var domainToBeDeleted = new AsciiDomainName("aabestbuyvius.com");
            var response2 = await client.YouAuth.RegisterDomain(domainToBeDeleted);
            Assert.IsTrue(response2.IsSuccessStatusCode, $"Failed status code.  Value was {response2.StatusCode}");
            Assert.IsNotNull(response2.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomains();
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var results = domainRegistrationResponse.Content;
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count() == 2);

            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain1.DomainName));
            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domainToBeDeleted.DomainName));

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain1, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);

            //register a client for domainToBeDeleted
            var domainToBeDeletedClientRegistrationResponse = await client.YouAuth.RegisterClient(domainToBeDeleted, "some friendly name");
            Assert.IsTrue(domainToBeDeletedClientRegistrationResponse.IsSuccessStatusCode);

            // now delete domainToBeDeleted
            var deleteResponse = await client.YouAuth.DeleteDomainRegistration(domainToBeDeleted);
            Assert.IsTrue(deleteResponse.IsSuccessStatusCode);

            var updatedDomainList = await client.YouAuth.GetDomains();
            Assert.That(updatedDomainList.IsSuccessStatusCode, Is.True);

            var results2 = updatedDomainList.Content;
            Assert.IsNotNull(results2);
            Assert.IsTrue(results2.Count() == 1);

            Assert.IsNotNull(results2.SingleOrDefault(d => d.Domain == domain1.DomainName));
            Assert.IsNull(results2.SingleOrDefault(d => d.Domain == domainToBeDeleted.DomainName), "domain2 should be deleted");

            //check that clients are gone

            var allClientsResponse = await client.YouAuth.GetRegisteredClients(domainToBeDeleted);
            Assert.IsTrue(allClientsResponse.IsSuccessStatusCode);
            Assert.IsNotNull(allClientsResponse.Content);

            Assert.IsTrue(allClientsResponse.Content.Count() == 0, "there should be one client for domain1ClientRegistrationResponse");
        }

        [Test]
        public async Task CanRevokeDomain_Then_RemoveRevocation()
        {
            var domain = new AsciiDomainName("amazoaac2om2.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);
            var response = await client.YouAuth.RegisterDomain(domain);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
            var activeDomain = domainRegistrationResponse.Content;

            Assert.That(activeDomain, Is.Not.Null);
            Assert.That(activeDomain.Domain, Is.EqualTo(domain.DomainName));

            Assert.IsFalse(activeDomain.IsRevoked);

            var revocationResponse = await client.YouAuth.RevokeDomain(domain);
            Assert.IsTrue(revocationResponse.IsSuccessStatusCode);

            var getRevokedDomainResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(getRevokedDomainResponse.IsSuccessStatusCode, Is.True);
            var revokedDomain = getRevokedDomainResponse.Content;
            Assert.IsNotNull(revokedDomain);
            Assert.IsTrue(revokedDomain.IsRevoked);

            var allowDomainResponse = await client.YouAuth.AllowDomain(domain);
            Assert.IsTrue(allowDomainResponse.IsSuccessStatusCode);

            var getAllowedDomainResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(getAllowedDomainResponse.IsSuccessStatusCode, Is.True);
            var allowedDomain = getAllowedDomainResponse.Content;
            Assert.IsNotNull(allowedDomain);
            Assert.IsFalse(allowedDomain.IsRevoked);
        }

        [Test]
        public async Task ConnectedIdentitySystemCircleNotGrantedToYouAuthDomain()
        {
            var domain = new AsciiDomainName("amazoom333.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var someDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var someCircle = await client.Membership.CreateCircle("Circle with valid permissions", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = someDrive.TargetDriveInfo,
                            Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                        }
                    }
                }
            });

            var response = await client.YouAuth.RegisterDomain(domain);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var domainRegistration = domainRegistrationResponse.Content;
            Assert.That(domainRegistration, Is.Not.Null);
            Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
            Assert.IsFalse(domainRegistration.IsRevoked);
            Assert.IsTrue(domainRegistration.Created > 0);
            // Assert.IsNotNull(domainRegistration.CorsHostName);

            //now grant the circle
            var grantCircleResponse = await client.YouAuth.GrantCircle(domain, someCircle.Id);
            Assert.IsTrue(grantCircleResponse.IsSuccessStatusCode);

            var getUpdatedDomainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(getUpdatedDomainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var updatedDomainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            var updatedDomainRegistration = updatedDomainRegistrationResponse.Content;
            Assert.IsNotNull(updatedDomainRegistration);
            var someCircleGrant = updatedDomainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == someCircle.Id);
            Assert.IsNotNull(someCircleGrant);
            Assert.IsTrue(someCircleGrant.DriveGrants.Count == someCircle.DriveGrants.Count());
            CollectionAssert.AreEquivalent(someCircleGrant.PermissionSet.Keys, someCircle.Permissions.Keys);

            // ensure the system circle was not granted
            Assert.IsNull(updatedDomainRegistration.CircleGrants.SingleOrDefault(c => c.CircleId == SystemCircleConstants.ConfirmedConnectionsCircleId),
                "The connected identities circle should not be granted to youauth domains");
        }

        [Test]
        public async Task CanGrantCircle()
        {
            var domain = new AsciiDomainName("amazoom4422.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var someDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var someCircle = await client.Membership.CreateCircle("Circle with valid permissions", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = someDrive.TargetDriveInfo,
                            Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                        }
                    }
                }
            });

            var response = await client.YouAuth.RegisterDomain(domain);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var domainRegistration = domainRegistrationResponse.Content;
            Assert.That(domainRegistration, Is.Not.Null);
            Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
            Assert.IsFalse(domainRegistration.IsRevoked);
            Assert.IsTrue(domainRegistration.Created > 0);
            // Assert.IsNotNull(domainRegistration.CorsHostName);

            //now grant the circle
            var grantCircleResponse = await client.YouAuth.GrantCircle(domain, someCircle.Id);
            Assert.IsTrue(grantCircleResponse.IsSuccessStatusCode);

            var getUpdatedDomainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(getUpdatedDomainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var updatedDomainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            var updatedDomainRegistration = updatedDomainRegistrationResponse.Content;
            Assert.IsNotNull(updatedDomainRegistration);
            var circle2Grant = updatedDomainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == someCircle.Id);
            Assert.IsNotNull(circle2Grant);
            Assert.IsTrue(circle2Grant.DriveGrants.Count == someCircle.DriveGrants.Count());
            CollectionAssert.AreEquivalent(circle2Grant.PermissionSet.Keys, someCircle.Permissions.Keys);
        }

        [Test]
        public async Task CanRevokeCircle()
        {
            var domain = new AsciiDomainName("amazeen2.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var circle1 = await client.Membership.CreateCircle("Circle with valid permissions", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadConnections })
            });

            var someDrive = await client.Drive.CreateDrive(TargetDrive.NewTargetDrive(), "Some drive",
                metadata: "",
                allowAnonymousReads: false,
                allowSubscriptions: false,
                ownerOnly: false);

            var circle2 = await client.Membership.CreateCircle("Circle with valid permissions", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadCircleMembership }),
                Drives = new List<DriveGrantRequest>()
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive()
                        {
                            Drive = someDrive.TargetDriveInfo,
                            Permission = DrivePermission.Write & DrivePermission.WriteReactionsAndComments
                        }
                    }
                }
            });

            var response = await client.YouAuth.RegisterDomain(domain, new List<GuidId>() { circle1.Id, circle2.Id });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var domainRegistration = domainRegistrationResponse.Content;
            Assert.That(domainRegistration, Is.Not.Null);
            Assert.That(domainRegistration.Domain, Is.EqualTo(domain.DomainName));
            Assert.IsFalse(domainRegistration.IsRevoked);
            Assert.IsTrue(domainRegistration.Created > 0);
            // Assert.IsNotNull(domainRegistration.CorsHostName);

            var circle1Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle1.Id);
            Assert.IsNotNull(circle1Grant);
            Assert.IsTrue(circle1Grant.DriveGrants.Count == (circle1.DriveGrants?.Count() ?? 0));
            Assert.IsTrue(circle1Grant.PermissionSet.Keys.Count == 1);
            CollectionAssert.AreEquivalent(circle1Grant.PermissionSet.Keys, circle1.Permissions.Keys);

            var circle2Grant = domainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle2.Id);
            Assert.IsNotNull(circle2Grant);
            Assert.IsTrue(circle2Grant.DriveGrants.Count == circle2.DriveGrants.Count());
            CollectionAssert.AreEquivalent(circle2Grant.PermissionSet.Keys, circle2.Permissions.Keys);

            // now revoke the circle 1

            var revokeCircle1Response = await client.YouAuth.RevokeCircle(domain, circle1.Id);
            Assert.IsTrue(revokeCircle1Response.IsSuccessStatusCode);

            var updatedDomainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(updatedDomainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var updatedDomainRegistration = updatedDomainRegistrationResponse.Content;
            Assert.IsNotNull(updatedDomainRegistration);

            Assert.IsTrue(updatedDomainRegistration.CircleGrants.Count() == 1, $"count was {updatedDomainRegistration.CircleGrants.Count()}");
            Assert.IsNull(updatedDomainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == circle1.Id));
        }

        [Test]
        public async Task CanRegisterClient()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazoaap4om2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain1, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);


            var allClientsResponse = await client.YouAuth.GetRegisteredClients(domain1);
            Assert.IsTrue(allClientsResponse.IsSuccessStatusCode);
            var allClients = allClientsResponse.Content;
            Assert.IsNotNull(allClients);
            Assert.IsNotNull(allClients.SingleOrDefault(c => c.Domain.DomainName == domain1.DomainName));
        }

        [Test]
        public async Task CanGetListOfClients()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazddoom2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            var domain2 = new AsciiDomainName("bestddbuyvius.com");
            var response2 = await client.YouAuth.RegisterDomain(domain2);
            Assert.IsTrue(response2.IsSuccessStatusCode, $"Failed status code.  Value was {response2.StatusCode}");
            Assert.IsNotNull(response2.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomains();
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var results = domainRegistrationResponse.Content;
            Assert.IsNotNull(results);
            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain1.DomainName));
            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain2.DomainName));

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain1, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);

            //register a client for domainToBeDeleted
            var domainToBeDeletedClientRegistrationResponse = await client.YouAuth.RegisterClient(domain2, "some friendly name");
            Assert.IsTrue(domainToBeDeletedClientRegistrationResponse.IsSuccessStatusCode);


            var domain1ClientsResponse = await client.YouAuth.GetRegisteredClients(domain1);
            Assert.IsTrue(domain1ClientsResponse.IsSuccessStatusCode);
            var domain1Clients = domain1ClientsResponse.Content;
            Assert.IsTrue(domain1Clients.Count == 1);


            var domain2ClientsResponse = await client.YouAuth.GetRegisteredClients(domain2);
            Assert.IsTrue(domain2ClientsResponse.IsSuccessStatusCode);
            var domain2Clients = domain2ClientsResponse.Content;
            Assert.IsTrue(domain2Clients.Count == 1);
        }

        [Test]
        public async Task CanDeleteYouAuthClient()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("accmazoom2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain1, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);

            var allClientsResponse = await client.YouAuth.GetRegisteredClients(domain1);
            Assert.IsTrue(allClientsResponse.IsSuccessStatusCode);
            Assert.IsNotNull(allClientsResponse.Content);
            Assert.IsNotNull(allClientsResponse.Content.SingleOrDefault(c => c.Domain.DomainName == domain1.DomainName));

            //delete the client
            var deleteClientResponse = await client.YouAuth.DeleteClient(domain1ClientRegistrationResponse.Content.AccessRegistrationId);
            Assert.IsTrue(deleteClientResponse.IsSuccessStatusCode);

            var emptyClientsResponse = await client.YouAuth.GetRegisteredClients(domain1);
            Assert.IsTrue(emptyClientsResponse.IsSuccessStatusCode);
            Assert.IsNotNull(emptyClientsResponse.Content);
            Assert.IsTrue(emptyClientsResponse.Content.Count == 0);
        }
    }
}