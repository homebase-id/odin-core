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
        [Ignore("convert to test the exchange grant")]
        public async Task CanValidateAppToken()
        {
            Guid appId = Guid.NewGuid();
            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.AddAppWithAllDrivePermissions(identity.DotYouId, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OldOwnerApi.AddAppClient(identity.DotYouId, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                Assert.That(validateResponse.Content.IsValid, Is.True);
            }
        }

        [Test]
        [Ignore("convert to test the exchange grant")]
        public async Task FailToValidateOnRevokedApp()
        {
            Guid appId = Guid.NewGuid();
            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.AddAppWithAllDrivePermissions(identity.DotYouId, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OldOwnerApi.AddAppClient(identity.DotYouId, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);

                Assert.That(validateResponse.Content.IsValid, Is.True);
            }

            await _scaffold.OldOwnerApi.RevokeSampleApp(identity.DotYouId, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);

                Assert.That(validateResponse.Content.IsValid, Is.False);
            }
        }

        [Test]
        [Ignore("convert to test the exchange grant")]
        public async Task FailToAuthenticateRevokedClient()
        {
            Guid appId = Guid.NewGuid();
            var identity = TestIdentities.Samwise;
            await _scaffold.OldOwnerApi.AddAppWithAllDrivePermissions(identity.DotYouId, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OldOwnerApi.AddAppClient(identity.DotYouId, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await appAuthSvc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                Assert.That(validateResponse.Content.IsValid, Is.True);
            }
        }

        [Test]
        [Ignore("convert to test the exchange grant")]
        public async Task CanRevokeAppMidSession()
        {
            var identity = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            await _scaffold.OldOwnerApi.AddAppWithAllDrivePermissions(identity.DotYouId, appId, TargetDrive.NewTargetDrive());
            var (clientAuthToken, sharedSecret) = await _scaffold.OldOwnerApi.AddAppClient(identity.DotYouId, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
            {
                var svc = RestService.For<IAppAuthenticationClient>(appClient);
                var validateResponse = await svc.ValidateClientToken(clientAuthToken.ToString());
                Assert.That(validateResponse.IsSuccessStatusCode, Is.True);
                Assert.That(validateResponse.Content, Is.Not.Null);
                var result = validateResponse.Content;

                Assert.That(result.IsValid, Is.True);
            }

            await _scaffold.OldOwnerApi.RevokeSampleApp(identity.DotYouId, appId);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity.DotYouId))
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