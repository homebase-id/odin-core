using Refit;
using System;
using NUnit.Framework;
using System.Reflection;
using System.Threading.Tasks;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Tests.OwnerApi.Authentication;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    [TestFixture]
    public class AppAuthenticationTests
    {
        private WebScaffold _scaffold;
        public static readonly Guid DefaultDrivePublicId = Guid.Parse("98408493-4440-0888-0000-001260004445");


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
        public async Task CanValidateAppToken()
        {
            Guid appId = Guid.NewGuid();
            var identity = TestIdentities.Samwise;
            await _scaffold.OwnerApi.AddApp(identity, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OwnerApi.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
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
            await _scaffold.OwnerApi.AddApp(identity, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OwnerApi.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);

                Assert.That(validateResponse.Content.IsValid, Is.True);
            }

            await _scaffold.OwnerApi.RevokeSampleApp(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
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
            await _scaffold.OwnerApi.AddApp(identity, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OwnerApi.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
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
            await _scaffold.OwnerApi.AddApp(identity, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OwnerApi.AddAppClient(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await svc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                var result = validateResponse.Content;

                Assert.That(result.IsValid, Is.True);
            }

            await _scaffold.OwnerApi.RevokeSampleApp(identity, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var svc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await svc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                var result = validateResponse.Content;

                Assert.That(result.IsValid, Is.False);
            }
        }
    }
}