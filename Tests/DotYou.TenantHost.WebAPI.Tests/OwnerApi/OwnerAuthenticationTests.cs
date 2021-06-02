using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using DotYou.Types;
using DotYou.Types.Admin;
using DotYou.Types.ApiClient;
using DotYou.Types.Cryptography;
using NUnit.Framework;
using Refit;

namespace DotYou.TenantHost.WebAPI.Tests.OwnerApi
{
    public class OwnerAuthenticationTests
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
        public async Task FrodoCanLogInAndOutOfHisDigitalIdentityHost()
        {
            DotYouIdentity user = scaffold.Frodo;
            using var client = scaffold.CreateHttpClient(user);
            var t = client.DefaultRequestHeaders.First(h => h.Key == DotYouHeaderNames.AuthToken).Value.SingleOrDefault();
            Assert.IsNotNull(t, "No token found in http client");

            var token = Guid.Parse(t);
            Assert.IsTrue(token != Guid.Empty);

            var svc = RestService.For<IOwnerAuthenticationClient>(client);
            var validationResponse = await svc.IsValid(token);
            Assert.IsTrue(validationResponse.IsSuccessStatusCode, $"Failed to execute validation auth token for {user}.  Token value is {token}");
            Assert.IsTrue(validationResponse.Content, $"Validation response for {user} was false.  Token value is {token}");

            await svc.Expire(token);

            var secondValidationResponse = await svc.IsValid(token);
            Assert.IsTrue(secondValidationResponse.IsSuccessStatusCode, $"Failed to execute validation for auth token for {user}.  Token value is {token}");
            Assert.IsFalse(secondValidationResponse.Content, $"Validation response for {user} was true when it should expired.  Token value is {token}");
        }

        [Test]
        public async Task FrodoCanGenerateNonce()
        {
            string identity = scaffold.Frodo;

            using HttpClient authClient = new();
            authClient.BaseAddress = new Uri($"https://{identity}");
            var svc = RestService.For<IOwnerAuthenticationClient>(authClient);

            var response = await svc.GenerateNonce();
            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed to get nonce for {identity}");

            var clientNonce = response.Content;

            //TODO encrypt password usign clientNonce
            string password = "p";

            AuthenticationNonceReply clientReply = new AuthenticationNonceReply()
            {
                Nonce64 = clientNonce.Nonce64,
                KeK64 = "todo",
                NonceHashedPassword64 = "should be hashed "
            };
                
            var authResponse = await svc.Authenticate(clientReply);
            
            Assert.IsTrue(authResponse.IsSuccessStatusCode);
            var authResult = authResponse.Content;
            

        }

        // public async void FrodoCanExtendHisAuthenticationTokenLife()
        // {
        //    //TODO: for this test to work, we need to support configurable session
        //    //times for unit tests (similar to how we support configurable data paths)
        // }
    }
}