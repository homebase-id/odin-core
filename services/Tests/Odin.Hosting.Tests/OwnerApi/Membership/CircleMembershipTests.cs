using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership;
using Odin.Core.Util;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Membership
{
    public class CircleMembershipTests
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
        public async Task CanGetListOfDomainsByCircle()
        {
            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);

            var recipient = TestIdentities.Samwise;
            var recipientClient = new OwnerApiClient(_scaffold.OldOwnerApi, recipient);

            //
            // Create a circle
            //
            var circle1 = await client.Membership.CreateCircle("Circle with valid permissions", new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new[] { PermissionKeys.ReadConnections })
            });

            //
            // Add youauth domain
            //
            const string youAuthDomain = "amazoonius.org";
            await AddYouAuthDomain(youAuthDomain, new List<GuidId>() { circle1.Id });

            // await ConnectIdentity()
            //Add an identity
            await client.Network.SendConnectionRequest(recipient, new List<GuidId>() { circle1.Id });
            await recipientClient.Network.AcceptConnectionRequest(_identity, new List<GuidId>());

            var getDomainsResponse = await client.Membership.GetDomainsInCircle(circle1.Id);
            var domains = getDomainsResponse.Content;
            Assert.IsNotNull(domains);
            Assert.IsTrue(domains.Count == 2);
            Assert.IsNotNull(domains.SingleOrDefault(d => d.Domain.DomainName == recipient.OdinId.DomainName && d.DomainType == DomainType.Identity),
                "missing identity domain");
            Assert.IsNotNull(domains.SingleOrDefault(d => d.Domain.DomainName == youAuthDomain && d.DomainType == DomainType.YouAuth),
                "missing youauth domain");
        }

        private async Task AddYouAuthDomain(string domainName, List<GuidId> circleIds)
        {
            var domain = new AsciiDomainName(domainName);

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);
            var response = await client.YouAuth.RegisterDomain(domain, circleIds);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);
        }

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
    }
}