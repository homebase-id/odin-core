using System.Collections.Generic;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Core.Services.Authorization.Permissions;
using Odin.Core.Services.Base;
using Odin.Core.Util;
using Odin.Hosting.Tests.OwnerApi.ApiClient;

namespace Odin.Hosting.Tests.OwnerApi.YouAuthDomains
{
    public class YouAuthDomainRegistrationTests
    {
        // private TestScaffold _scaffold;

        private WebScaffold _scaffold;

        private readonly TestIdentity _identity = TestIdentities.Frodo;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
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

            var client = new YouAuthDomainApiClient(_scaffold.OldOwnerApi, _identity);
            var response = await client.RegisterDomain(domain, new PermissionSetGrantRequest());

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var domainRegistrationResponse = await client.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.IsSuccessStatusCode, Is.True);
            Assert.That(domainRegistrationResponse.Content, Is.Not.Null);
            Assert.That(domainRegistrationResponse.Content.Domain, Is.EqualTo(domain.DomainName));
        }

        [Test]
        public async Task FailWhenUseTransitPermissionWhenCreatingYouAuthDomain()
        {
            var domain = new AsciiDomainName("amazoomius.com");

            var client = new YouAuthDomainApiClient(_scaffold.OldOwnerApi, _identity);
            var response = await client.RegisterDomain(domain, new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.UseTransit }),
                Drives = null
            });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"Status code should be Bad Request but was {response.StatusCode}");
            Assert.IsNull(response.Content);

            var domainRegistrationResponse = await client.GetDomainRegistration(domain);
            Assert.That(domainRegistrationResponse.StatusCode, Is.EqualTo(HttpStatusCode.NotFound),
                $"Status code should be not found but was {response.StatusCode}");
        }

        [Test]
        public async Task FailWhenUseTransitPermissionWhenUpdatingYouAuthDomainPermissions()
        {
            var domain = new AsciiDomainName("amazoom.com");

            var client = new YouAuthDomainApiClient(_scaffold.OldOwnerApi, _identity);
            var response = await client.RegisterDomain(domain, new PermissionSetGrantRequest());

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var updateResponse = await client.UpdatePermissions(domain, new PermissionSetGrantRequest()
            {
                PermissionSet = new PermissionSet(new List<int>() { PermissionKeys.UseTransit })
            });

            Assert.IsTrue(updateResponse.StatusCode == HttpStatusCode.BadRequest, $"Status code should be bad request but was ${updateResponse.StatusCode}");

            var domainRegistrationResponse = await client.GetDomainRegistration(domain);
            //be sure the key was not added
            Assert.IsFalse(domainRegistrationResponse.Content.Grant.PermissionSet?.Keys?.Contains(PermissionKeys.UseTransit) ?? false);
        }
    }
}