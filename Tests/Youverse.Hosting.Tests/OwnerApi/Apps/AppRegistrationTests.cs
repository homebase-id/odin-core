using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Serialization;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Authorization.ExchangeGrants;
using Youverse.Hosting.Controllers.OwnerToken.AppManagement;

namespace Youverse.Hosting.Tests.OwnerApi.Apps
{
    public class AppRegistrationTests
    {
        // private TestScaffold _scaffold;

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
        public async Task RegisterNewApp()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-register";
            var newId = await AddSampleAppNoDrive(appId, name);
        }

        [Test]
        public async Task RevokeAppRegistration()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-revoke";

            var newId = await AddSampleAppNoDrive(appId, name);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.DotYouId, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var revokeResponse = await svc.RevokeApp(new GetAppRequest() { AppId = appId });

                Assert.IsTrue(revokeResponse.IsSuccessStatusCode);
                Assert.IsTrue(revokeResponse.Content?.Success);

                var savedApp = await GetSampleApp(appId);
                Assert.IsNotNull(savedApp);
                Assert.IsTrue(savedApp.IsRevoked);
            }
        }

        [Test]
        public async Task RegisterAppOnClient()
        {
            var identity = TestIdentities.Frodo;

            var rsa = new RsaFullKeyData(ref RsaKeyListManagement.zeroSensitiveKey, 1);
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-reg-app-device";

            await AddSampleAppNoDrive(appId, name);

            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(identity.DotYouId, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);

                var request = new AppClientRegistrationRequest()
                {
                    AppId = appId,
                    ClientPublicKey64 = Convert.ToBase64String(rsa.publicKey),
                    ClientFriendlyName = "Some phone"
                };

                var regResponse = await svc.RegisterAppOnClient(request);
                Assert.IsTrue(regResponse.IsSuccessStatusCode);
                Assert.IsNotNull(regResponse.Content);

                var reply = regResponse.Content;
                var decryptedData = rsa.Decrypt(ref RsaKeyListManagement.zeroSensitiveKey, reply.Data); // TODO

                //only supporting version 1 for now
                Assert.That(reply.EncryptionVersion, Is.EqualTo(1));
                Assert.That(reply.Token, Is.Not.EqualTo(Guid.Empty));
                Assert.That(decryptedData, Is.Not.Null);
                Assert.That(decryptedData.Length, Is.EqualTo(49));
                
                var (tokenPortableBytes, sharedSecret) = ByteArrayUtil.Split(decryptedData, 33, 16);

                ClientAuthenticationToken authenticationResult = ClientAuthenticationToken.FromPortableBytes(tokenPortableBytes);

                Assert.False(authenticationResult.Id == Guid.Empty);
                Assert.IsNotNull(authenticationResult.AccessTokenHalfKey);
                Assert.That(authenticationResult.AccessTokenHalfKey.GetKey().Length, Is.EqualTo(16));
                Assert.IsTrue(authenticationResult.AccessTokenHalfKey.IsSet());
                
                Assert.IsNotNull(sharedSecret);
                Assert.That(sharedSecret.Length, Is.EqualTo(16));
            }
        }

        private async Task<RedactedAppRegistration> AddSampleAppNoDrive(Guid applicationId, string name)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.DotYouId, out var ownerSharedSecret))
            {

                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var request = new AppRegistrationRequest
                {
                    AppId = applicationId,
                    Name = name,
                    PermissionSet = null,
                    Drives = null
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.AppId == request.AppId);
                Assert.IsTrue(savedApp.Name == request.Name);

                return appReg;
            }
        }

        private async Task<RedactedAppRegistration> GetSampleApp(Guid appId)
        {
            using (var client = _scaffold.OwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.DotYouId, out var ownerSharedSecret))
            {
                var svc = _scaffold.RestServiceFor<IAppRegistrationClient>(client, ownerSharedSecret);
                var appResponse = await svc.GetRegisteredApp(new GetAppRequest() { AppId = appId });
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {appId}");
                Assert.IsNotNull(appResponse.Content, $"Could not retrieve the app {appId}");
                var savedApp = appResponse.Content;
                return savedApp;
            }
        }
    }
}