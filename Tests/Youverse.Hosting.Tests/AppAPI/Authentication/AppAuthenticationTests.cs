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
            var identity = DotYouIdentities.Samwise;
            Guid authCode;
            var appDevice = new AppDevice()
            {
                ApplicationId = _scaffold.ApplicationId,
                DeviceUid = _scaffold.DeviceUid
            };

            await _scaffold.AddSampleApp(identity,  true);
            await _scaffold.AddAppDevice(identity);
            
            using (var ownerClient = _scaffold.CreateOwnerApiHttpClient(identity))
            {
                var ownerAuthSvc = RestService.For<IOwnerAuthenticationClient>(ownerClient);
                var authCodeResponse = await ownerAuthSvc.CreateAppSession(appDevice);
                Assert.That(authCodeResponse.IsSuccessStatusCode, Is.True);
                
                authCode = authCodeResponse.Content;
                Assert.That(authCode, Is.Not.EqualTo(Guid.Empty));
            }

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);

                var request = new AuthCodeExchangeRequest()
                {
                    AuthCode = authCode,
                    AppDevice = appDevice
                };
                
                var authResultResponse = await appAuthSvc.ExchangeAuthCode(request);
                Assert.That(authResultResponse.IsSuccessStatusCode, Is.True);
                var authResult = authResultResponse.Content;
                Assert.That(authResult, Is.Not.Null);
                Assert.That(DotYouAuthenticationResult.TryParse(authResult, out var _), Is.True);
            }
        }
        
        [Test]
        public async Task CannotReplyAuthCode()
        {
            var identity = DotYouIdentities.Samwise;
            Guid authCode;
            var appDevice = new AppDevice()
            {
                ApplicationId = _scaffold.ApplicationId,
                DeviceUid = _scaffold.DeviceUid
            };

            await _scaffold.AddSampleApp(identity,  true);
            await _scaffold.AddAppDevice(identity);
            
            using (var ownerClient = _scaffold.CreateOwnerApiHttpClient(identity))
            {
                var ownerAuthSvc = RestService.For<IOwnerAuthenticationClient>(ownerClient);
                var authCodeResponse = await ownerAuthSvc.CreateAppSession(appDevice);
                Assert.That(authCodeResponse.IsSuccessStatusCode, Is.True);
                
                authCode = authCodeResponse.Content;
                Assert.That(authCode, Is.Not.EqualTo(Guid.Empty));
            }

            using (var appClient = _scaffold.CreateAnonymousApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);

                var request = new AuthCodeExchangeRequest()
                {
                    AuthCode = authCode,
                    AppDevice = appDevice
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
        public async Task AuthorizationCodeExpires()
        {
            var identity = DotYouIdentities.Samwise;
            Guid authCode;
            var appDevice = new AppDevice()
            {
                ApplicationId = _scaffold.ApplicationId,
                DeviceUid = _scaffold.DeviceUid
            };

            await _scaffold.AddSampleApp(identity,  true);
            await _scaffold.AddAppDevice(identity);
            
            using (var ownerClient = _scaffold.CreateOwnerApiHttpClient(identity))
            {
                var ownerAuthSvc = RestService.For<IOwnerAuthenticationClient>(ownerClient);
                var authCodeResponse = await ownerAuthSvc.CreateAppSession(appDevice);
                Assert.That(authCodeResponse.IsSuccessStatusCode, Is.EqualTo(true));
                
                authCode = authCodeResponse.Content;
                Assert.That(authCode, Is.Not.EqualTo(Guid.Empty));
            }

            //TODO: this bound to the value in AppAuthenticationService for AppAuthAuthorizationCode
            Thread.Sleep(16 * 1000);
            
            using (var appClient = _scaffold.CreateOwnerApiHttpClient(identity))
            {
                var appAuthSvc = RestService.For<IAppAuthenticationClient>(appClient);

                var request = new AuthCodeExchangeRequest()
                {
                    AuthCode = authCode,
                    AppDevice = appDevice
                };
                
                var authResultResponse = await appAuthSvc.ExchangeAuthCode(request);
                Assert.IsTrue(authResultResponse.IsSuccessStatusCode);
                var authResult = authResultResponse.Content;
                Assert.IsNotNull(authResult);
            }
        }
    }
}