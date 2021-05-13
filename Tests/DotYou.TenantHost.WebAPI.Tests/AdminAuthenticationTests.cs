using System;
using System.Linq;
using DotYou.Types;
using NUnit.Framework;
using System.Reflection;
using System.Threading.Tasks;
using DotYou.Types.Admin;
using DotYou.Types.ApiClient;
using Refit;

namespace DotYou.TenantHost.WebAPI.Tests
{
    public class AdminAuthenticationTests
    {
        private TestScaffold scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            scaffold = new TestScaffold(folder);
            scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task FrodoCanAuthenticateOnHisDigitalIdentityHost()
        {
            DotYouIdentity user = scaffold.Frodo;
            using var client = scaffold.CreateHttpClient(user);
            var t = client.DefaultRequestHeaders.First(h => h.Key == DotYouHeaderNames.AuthToken).Value.SingleOrDefault();
            Assert.IsNotNull(t, "No token found in http client");

            var token = Guid.Parse(t);
            Assert.IsTrue(token != Guid.Empty);

            var svc = RestService.For<IAdminAuthenticationClient>(client);
            var validationResponse = await svc.IsValid(token);
            Assert.IsTrue(validationResponse.IsSuccessStatusCode, $"Failed to execute validation auth token for {user}.  Token value is {token}");
            Assert.IsTrue(validationResponse.Content, $"Validation response for {user} was false.  Token value is {token}");
        }

        [Test]
        public async Task FrodoCanLogoutOfHisDigitalIdentityHost()
        {
            DotYouIdentity user = scaffold.Frodo;
            using var client = scaffold.CreateHttpClient(user);
            var svc = RestService.For<IAdminAuthenticationClient>(client);

            string password = "";
            var authenticateResponse = await svc.Authenticate(password);

            Assert.IsTrue(authenticateResponse.IsSuccessStatusCode, "Failed to authenticate frodo");
            Assert.IsTrue(authenticateResponse.Content != Guid.Empty);

            Guid token = authenticateResponse.Content;
            await svc.Expire(token);

            var validationResponse = await svc.IsValid(token);
            Assert.IsTrue(validationResponse.IsSuccessStatusCode, $"Failed to execute validation for auth token for {user}.  Token value is {token}");
            Assert.IsFalse(validationResponse.Content, $"Validation response for {user} was true when it should expired.  Token value is {token}");
        }

        // public async void FrodoCanExtendHisAuthenticationTokenLife()
        // {
        //    //TODO: for this test to work, we need to support configurable session
        //    //times for unit tests (similar to how we support configurable data paths)
        // }
    }
}