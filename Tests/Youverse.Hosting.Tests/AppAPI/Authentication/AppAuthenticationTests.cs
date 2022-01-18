using Refit;
using System;
using NUnit.Framework;
using System.Reflection;
using System.Threading.Tasks;
using Youverse.Core.Services.Authentication;
using Youverse.Hosting.Tests.OwnerApi.Authentication;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    [TestFixture]
    public class AppAuthenticationTests
    {
        private TestScaffold _scaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new TestScaffold(folder);
            _scaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _scaffold.RunAfterAnyTests();
        }

        [Test]
        public async Task CanValidateAppToken()
        {
            Guid appId = Guid.NewGuid();
            var identity = TestIdentities.Samwise;
            await _scaffold.AddApp(identity, appId);
            var (authResult, sharedSecret) = await _scaffold.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(authResult.SessionToken);
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                Assert.That(validateResponse.Content.IsValid, Is.True);
            }
        }

        [Test]
        public async Task FailToValidateOnRevokedApp()
        {
            Guid appId = Guid.NewGuid();
            var identity = TestIdentities.Samwise;
            await _scaffold.AddApp(identity, appId);
            var (authResult, sharedSecret) = await _scaffold.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(authResult.SessionToken);
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);

                Assert.That(validateResponse.Content.IsValid, Is.True);
            }
            
            await _scaffold.RevokeSampleApp(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(authResult.SessionToken);
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);

                Assert.That(validateResponse.Content.IsValid, Is.False);
            }
        }

        [Test]
        public async Task FailToAuthenticateRevokedClient()
        {
            Guid appId = Guid.NewGuid();
            var identity = TestIdentities.Samwise;
            await _scaffold.AddApp(identity, appId);
            var (authResult, sharedSecret) = await _scaffold.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(authResult.SessionToken);
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                Assert.That(validateResponse.Content.IsValid, Is.True);
            }
        }

        [Test]
        public async Task CanRevokeAppMidSession()
        {
            var identity = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            await _scaffold.AddApp(identity, appId);
            var (authResult, sharedSecret) = await _scaffold.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await svc.ValidateClientToken(authResult.SessionToken);
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                var result = validateResponse.Content;

                Assert.That(result.IsValid, Is.True);
            }

            await _scaffold.RevokeSampleApp(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await svc.ValidateClientToken(authResult.SessionToken);
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                var result = validateResponse.Content;

                Assert.That(result.IsValid, Is.False);
            }
        }
    }
}