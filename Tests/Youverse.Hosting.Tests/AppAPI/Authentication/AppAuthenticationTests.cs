using Refit;
using System;
using NUnit.Framework;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authentication.AppAuth;
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
        public async Task CanAuthenticateApp()
        {
            var identity = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            byte[] deviceUid = Guid.NewGuid().ToByteArray();

            await _scaffold.AddSampleApp(identity, appId);
            await _scaffold.AddAppDevice(identity, appId, deviceUid);
            Guid authCode = await _scaffold.CreateAppSession(identity, appId, deviceUid);

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);

                var request = new AuthCodeExchangeRequest()
                {
                    AuthCode = authCode,
                    AppDevice = new AppDevice()
                    {
                        ApplicationId = appId,
                        DeviceUid = deviceUid
                    }
                };

                var authResultResponse = await appAuthSvc.ExchangeAuthCode(request);
                Assert.That(authResultResponse.IsSuccessStatusCode, Is.True);
                var authResult = authResultResponse.Content;
                Assert.That(authResult, Is.Not.Null);
                Assert.That(DotYouAuthenticationResult.TryParse(authResult, out var _), Is.True);
            }
        }

        [Test]
        public async Task CannotReplayAuthorizationCode()
        {
            var identity = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            byte[] deviceUid = Guid.NewGuid().ToByteArray();

            await _scaffold.AddSampleApp(identity, appId);
            await _scaffold.AddAppDevice(identity, appId, deviceUid);
            Guid authCode = await _scaffold.CreateAppSession(identity, appId, deviceUid);


            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);

                var request = new AuthCodeExchangeRequest()
                {
                    AuthCode = authCode,
                    AppDevice = new AppDevice()
                    {
                        ApplicationId = appId,
                        DeviceUid = deviceUid
                    }
                };

                var authResultResponse = await appAuthSvc.ExchangeAuthCode(request);
                Assert.That(authResultResponse.IsSuccessStatusCode, Is.True);
                var authResult = authResultResponse.Content;
                Assert.That(authResult, Is.Not.Null);
                Assert.That(DotYouAuthenticationResult.TryParse(authResult, out var _), Is.True);

                //run that same request
                var authResultReplayResponse = await appAuthSvc.ExchangeAuthCode(request);
                Assert.That(authResultReplayResponse.IsSuccessStatusCode, Is.False);
            }
        }

        [Test]
        public async Task CannotUseExpiredAuthorizationCode()
        {
            var identity = TestIdentities.Samwise;
            Guid appId = Guid.NewGuid();
            byte[] deviceUid = Guid.NewGuid().ToByteArray();

            await _scaffold.AddSampleApp(identity, appId);
            await _scaffold.AddAppDevice(identity, appId, deviceUid);
            Guid authCode = await _scaffold.CreateAppSession(identity, appId, deviceUid);

            //TODO: this bound to the value in AppAuthenticationService for AppAuthAuthorizationCode
            Thread.Sleep(16 * 1000);

            using (var appClient = _scaffold.CreateOwnerApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);

                var request = new AuthCodeExchangeRequest()
                {
                    AuthCode = authCode,
                    AppDevice = new AppDevice()
                    {
                        ApplicationId = appId,
                        DeviceUid = deviceUid
                    }
                };

                var authResultResponse = await appAuthSvc.ExchangeAuthCode(request);
                Assert.That(authResultResponse.IsSuccessStatusCode, Is.False);
            }
        }

        [Test]
        public async Task CannotAuthenticateRevokedApp()
        {
            var identity = TestIdentities.Samwise;

            Guid appId = Guid.NewGuid();
            byte[] deviceUid = Guid.NewGuid().ToByteArray();

            await _scaffold.AddSampleApp(identity, appId);
            await _scaffold.AddAppDevice(identity, appId, deviceUid);
            Guid authCode = await _scaffold.CreateAppSession(identity, appId, deviceUid);
            await _scaffold.RevokeSampleApp(identity, appId);

            using (var ownerClient = _scaffold.CreateOwnerApiHttpClient(identity))
            {
                var ownerAuthSvc = RestService.For<IOwnerAuthenticationClient>(ownerClient);
                var authCodeResponse = await ownerAuthSvc.CreateAppSession(new AppDevice()
                {
                    ApplicationId = appId,
                    DeviceUid = deviceUid
                });
                Assert.That(authCodeResponse.IsSuccessStatusCode, Is.False);
            }
        }
    }
}