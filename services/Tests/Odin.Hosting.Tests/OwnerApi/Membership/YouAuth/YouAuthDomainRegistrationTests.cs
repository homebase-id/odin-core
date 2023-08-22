using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.ExchangeGrants;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Services.Drives;
using Odin.Core.Services.Membership.Connections;
using Odin.Core.Util;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.Membership.YouAuth
{
    public class YouAuthDomainRegistrationTests
    {
        // private TestScaffold _scaffold;

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
        public async Task RegisterNewDomain()
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
        public async Task RegisterNewDomainWithCircle()
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
    }
}