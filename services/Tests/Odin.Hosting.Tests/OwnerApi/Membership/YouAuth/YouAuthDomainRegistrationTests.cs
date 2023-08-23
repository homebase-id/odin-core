using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Storage.SQLite.DriveDatabase;
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
            string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
            _scaffold = new WebScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanRegisterNewDomain()
        {
            var domain = new AsciiDomainName("amazoom2.com");

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);
            var response = await client.YouAuth.RegisterDomain(domain);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
            Assert.That(domainRegistrationResponse.Content, Is.Not.Null);
            Assert.That(domainRegistrationResponse.Content.Domain, Is.EqualTo(domain.DomainName));
        }

        [Test]
        public async Task CanRegisterNewDomainWithCircle()
        {
            var domain = new AsciiDomainName("amazoom2.com");

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

            var domain1 = new AsciiDomainName("amazoom2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            var domain2 = new AsciiDomainName("bestbuyvius.com");
            var response2 = await client.YouAuth.RegisterDomain(domain2);
            Assert.IsTrue(response2.IsSuccessStatusCode, $"Failed status code.  Value was {response2.StatusCode}");
            Assert.IsNotNull(response2.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomains();
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var results = domainRegistrationResponse.Content;
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count() == 2);

            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain1.DomainName));
            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain2.DomainName));
        }

        [Test]
        public async Task CanDeleteDomain()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazoom2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            var domainToBeDeleted = new AsciiDomainName("bestbuyvius.com");
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

            var allClientsResponse = await client.YouAuth.GetRegisteredClients();
            Assert.IsTrue(allClientsResponse.IsSuccessStatusCode);
            Assert.IsNotNull(allClientsResponse.Content);

            Assert.IsTrue(allClientsResponse.Content.Count() == 1, "there should be one client for domain1ClientRegistrationResponse");
            var noMatchingClients = allClientsResponse.Content.TrueForAll(c => c.Domain.DomainName != domainToBeDeleted.DomainName);
            Assert.IsTrue(noMatchingClients, "there should be no clients for domainToBeDeleted");
        }

        [Test]
        public async Task CanRevokeDomain_Then_RemoveRevocation()
        {
            var domain = new AsciiDomainName("amazoom2.com");

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
        public async Task CanGrantCircle()
        {
            var domain = new AsciiDomainName("amazoom2.com");

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

            var updatedDomainRegistration = domainRegistrationResponse.Content;
            Assert.IsNotNull(updatedDomainRegistration);
            var circle2Grant = updatedDomainRegistration.CircleGrants.SingleOrDefault(cg => cg.CircleId == someCircle.Id);
            Assert.IsNotNull(circle2Grant);
            Assert.IsTrue(circle2Grant.DriveGrants.Count == someCircle.DriveGrants.Count());
            CollectionAssert.AreEquivalent(circle2Grant.PermissionSet.Keys, someCircle.Permissions.Keys);
        }

        [Test]
        public async Task CanRevokeCircle()
        {
            var domain = new AsciiDomainName("amazoom2.com");

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

            var updatedDomainRegistration = domainRegistrationResponse.Content;
            Assert.IsNotNull(updatedDomainRegistration);

            Assert.IsTrue(updatedDomainRegistration.CircleGrants.Count() == 1);
            Assert.IsNull(updatedDomainRegistration.CircleGrants.SingleOrDefault(cg=>cg.CircleId == circle1.Id));
        }

        [Test]
        public async Task CanRegisterClient()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazoom2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain1, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);


            var allClientsResponse = await client.YouAuth.GetRegisteredClients();
            Assert.IsTrue(allClientsResponse.IsSuccessStatusCode);
            var allClients = allClientsResponse.Content;
            Assert.IsNotNull(allClients);
            Assert.IsTrue(allClients.Count() == 1);

            Assert.IsNotNull(allClients.SingleOrDefault(c => c.Domain.DomainName == domain1.DomainName));
        }

        [Test]
        public async Task CanGetListOfClients()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazoom2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            var domain2 = new AsciiDomainName("bestbuyvius.com");
            var response2 = await client.YouAuth.RegisterDomain(domain2);
            Assert.IsTrue(response2.IsSuccessStatusCode, $"Failed status code.  Value was {response2.StatusCode}");
            Assert.IsNotNull(response2.Content);

            var domainRegistrationResponse = await client.YouAuth.GetDomains();
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);

            var results = domainRegistrationResponse.Content;
            Assert.IsNotNull(results);
            Assert.IsTrue(results.Count() == 2);

            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain1.DomainName));
            Assert.IsNotNull(results.SingleOrDefault(d => d.Domain == domain2.DomainName));

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain1, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);

            //register a client for domainToBeDeleted
            var domainToBeDeletedClientRegistrationResponse = await client.YouAuth.RegisterClient(domain2, "some friendly name");
            Assert.IsTrue(domainToBeDeletedClientRegistrationResponse.IsSuccessStatusCode);


            var allClientsResponse = await client.YouAuth.GetRegisteredClients();
            Assert.IsTrue(allClientsResponse.IsSuccessStatusCode);
            var allClients = allClientsResponse.Content;
            Assert.IsNotNull(allClients);
            Assert.IsTrue(allClients.Count() == 2);

            Assert.IsNotNull(allClients.SingleOrDefault(c => c.Domain.DomainName == domain1.DomainName));
            Assert.IsNotNull(allClients.SingleOrDefault(c => c.Domain.DomainName == domain2.DomainName));
        }

        [Test]
        public async Task CanDeleteYouAuthClient()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var domain1 = new AsciiDomainName("amazoom2.com");
            var response1 = await client.YouAuth.RegisterDomain(domain1);
            Assert.IsTrue(response1.IsSuccessStatusCode, $"Failed status code.  Value was {response1.StatusCode}");
            Assert.IsNotNull(response1.Content);

            //register a client for domain1
            var domain1ClientRegistrationResponse = await client.YouAuth.RegisterClient(domain1, "some friendly name");
            Assert.IsTrue(domain1ClientRegistrationResponse.IsSuccessStatusCode);

            var allClientsResponse = await client.YouAuth.GetRegisteredClients();
            Assert.IsTrue(allClientsResponse.IsSuccessStatusCode);
            Assert.IsNotNull(allClientsResponse.Content);
            Assert.IsTrue(allClientsResponse.Content.Count() == 1, "there should be one client for domain1ClientRegistrationResponse");

            //delete the client
            var deleteClientResponse = await client.YouAuth.DeleteClient(domain1ClientRegistrationResponse.Content.AccessRegistrationId);
            Assert.IsTrue(deleteClientResponse.IsSuccessStatusCode);

            var emptyClientsResponse = await client.YouAuth.GetRegisteredClients();
            Assert.IsTrue(emptyClientsResponse.IsSuccessStatusCode);
            Assert.IsNotNull(emptyClientsResponse.Content);
            Assert.IsFalse(emptyClientsResponse.Content.Any(), "there should be no clients");
        }
    }
}