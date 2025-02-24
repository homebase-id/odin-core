using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Core;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Membership;
using Odin.Services.Membership.CircleMembership;
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

            //Add an identity
            await client.Network.SendConnectionRequestTo(recipient, new List<GuidId>() { circle1.Id });
            await recipientClient.Network.AcceptConnectionRequest(_identity, new List<GuidId>());

            var getDomainsResponse = await client.Membership.GetDomainsInCircle(circle1.Id);
            var domains = getDomainsResponse.Content;
            ClassicAssert.IsNotNull(domains);
            ClassicAssert.IsTrue(domains.Count == 2);

            var identityRecord = domains.SingleOrDefault(d => d.Domain.DomainName == recipient.OdinId.DomainName && d.DomainType == DomainType.Identity);
            ClassicAssert.IsNotNull(identityRecord, "missing identity domain");
            ClassicAssert.IsTrue(identityRecord.CircleGrant.CircleId == circle1.Id);
            ClassicAssert.IsNotNull(identityRecord.CircleGrant);
            ClassicAssert.IsTrue((identityRecord.CircleGrant.DriveGrants?.Count() ?? 0) == 0);

            var youAuthDomainRecord = domains.SingleOrDefault(d => d.Domain.DomainName == youAuthDomain && d.DomainType == DomainType.YouAuth);
            ClassicAssert.IsNotNull(youAuthDomainRecord, "missing identity domain");
            ClassicAssert.IsNotNull(youAuthDomainRecord.CircleGrant);
            ClassicAssert.IsTrue(youAuthDomainRecord.CircleGrant.CircleId == circle1.Id);
            ClassicAssert.IsTrue((youAuthDomainRecord.CircleGrant.DriveGrants?.Count() ?? 0) == 0);
        }

        private async Task AddYouAuthDomain(string domainName, List<GuidId> circleIds)
        {
            var domain = new AsciiDomainName(domainName);

            var client = new OwnerApiClient(_scaffold.OldOwnerApi, _identity);
            var response = await client.YouAuth.RegisterDomain(domain, circleIds);

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            ClassicAssert.IsNotNull(response.Content);
        }
    }
}