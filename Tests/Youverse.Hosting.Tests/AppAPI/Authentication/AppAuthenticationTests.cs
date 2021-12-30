using Refit;
using System;
using NUnit.Framework;
using System.Reflection;
using System.Threading.Tasks;
using Youverse.Core.Services.Authentication.AppAuth;
using Youverse.Hosting.Tests.OwnerApi.Authentication;
using Youverse.Hosting.Tests.OwnerApi.Provisioning;

namespace Youverse.Hosting.Tests.AppAPI.Authentication
{
    [TestFixture]
    public class AppAuthenticationTests
    {
        private OwnerConsoleTestScaffold _ownerTestScaffold;
        private AppTestScaffold _appTestScaffold;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _ownerTestScaffold = new OwnerConsoleTestScaffold(folder);
            _ownerTestScaffold.RunBeforeAnyTests();

            _appTestScaffold = new AppTestScaffold(folder);
            _appTestScaffold.RunBeforeAnyTests();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            _ownerTestScaffold.RunAfterAnyTests();
            _appTestScaffold.RunBeforeAnyTests();
        }

        [Test]
        public async Task CanAuthenticateApp()
        {
            var identity = DotYouIdentities.Samwise;
            Guid authCode;
            var appDevice = new AppDevice()
            {
                ApplicationId = Guid.NewGuid(),
                DeviceUid = Guid.NewGuid().ToByteArray()
            };
            
            using (var ownerClient = _ownerTestScaffold.CreateHttpClient(identity))
            {
                var provisioningClient = RestService.For<IProvisioningClient>(ownerClient);
                await provisioningClient.ConfigureDefaults();

                var ownerAuthSvc = RestService.For<IOwnerAuthenticationClient>(ownerClient);
                var authCodeResponse = await ownerAuthSvc.CreateAppSession(appDevice);
                Assert.That(authCodeResponse.IsSuccessStatusCode, Is.EqualTo(true));
                
                authCode = authCodeResponse.Content;
                Assert.That(authCode, Is.Not.EqualTo(Guid.Empty));
            }

            using (var appClient = _appTestScaffold.CreateHttpClient(identity))
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