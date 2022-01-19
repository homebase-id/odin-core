using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Crypto;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authentication;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Controllers.Owner.AppManagement;

namespace Youverse.Hosting.Tests.OwnerApi.Apps
{
    public class AppRegistrationTests
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
        public async Task RegisterNewApp()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-register";
            var newId = await AddSampleAppNoDrive(appId, name);
        }

        [Test]
        public async Task RegisterNewAppWithDrive()
        {
            Assert.Inconclusive("TODO");
        }

        [Test]
        public async Task RevokeAppRegistration()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-revoke";

            var newId = AddSampleAppNoDrive(appId, name);

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);
                var revokeResponse = await svc.RevokeApp(appId);

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

            using (var client = _scaffold.CreateOwnerApiHttpClient(identity))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);

                var request = new AppClientRegistrationRequest()
                {
                    ApplicationId = appId,
                    ClientPublicKey64 = Convert.ToBase64String(rsa.publicKey)
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
                Assert.That(decryptedData.Length, Is.EqualTo(32));

                var (clientKek, sharedSecret) = ByteArrayUtil.Split(decryptedData, 16, 16);

                Assert.IsNotNull(clientKek);
                Assert.IsNotNull(sharedSecret);
                Assert.That(clientKek.Length, Is.EqualTo(16));
                Assert.That(sharedSecret.Length, Is.EqualTo(16));
                
                var savedAppClientResponse = await svc.GetRegisteredAppClient(reply.Token);
                Assert.IsTrue(savedAppClientResponse.IsSuccessStatusCode);
                var savedAppClient = savedAppClientResponse.Content;
                
                Assert.IsNotNull(savedAppClient);
                Assert.IsTrue(savedAppClient.ApplicationId == appId);
                Assert.IsFalse(savedAppClient.IsRevoked);
                Assert.IsFalse(savedAppClient.Id == Guid.Empty);

            }
        }

        private async Task<AppRegistrationResponse> AddSampleAppNoDrive(Guid applicationId, string name)
        {
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);
                var request = new AppRegistrationRequest
                {
                    Name = name,
                    ApplicationId = applicationId
                };

                var response = await svc.RegisterApp(request);

                Assert.IsTrue(response.IsSuccessStatusCode);
                var appReg = response.Content;
                Assert.IsNotNull(appReg);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.ApplicationId == request.ApplicationId);
                Assert.IsTrue(savedApp.Name == request.Name);
                Assert.IsTrue(savedApp.DriveId == null);

                return appReg;
            }
        }

        private async Task<AppRegistrationResponse> GetSampleApp(Guid applicationId)
        {
            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);
                var appResponse = await svc.GetRegisteredApp(applicationId);
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                Assert.IsNotNull(appResponse.Content, $"Could not retrieve the app {applicationId}");
                var savedApp = appResponse.Content;
                return savedApp;
            }
        }
    }
}