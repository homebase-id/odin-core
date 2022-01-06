using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
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
        public async Task RegisterAppOnDevice()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-reg-app-device";

            await AddSampleAppNoDrive(appId, name);

            using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo))
            {
                var svc = RestService.For<IAppRegistrationClient>(client);


                //TODO: rsa encrypt the shared secret

                var payload = new AppDeviceRegistrationRequest()
                {
                    ApplicationId = appId,
                    DeviceId64 = Convert.ToBase64String(Guid.Parse("a917c85f-732d-4991-a3d9-5aeba3e89f32").ToByteArray()),
                    SharedSecretKey64 = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
                };

                var regResponse = await svc.RegisterAppOnDevice(payload);
                Assert.IsTrue(regResponse.IsSuccessStatusCode);

                var savedAppDeviceResponse = await svc.GetRegisteredAppDevice(appId, payload.DeviceId64);

                Assert.IsTrue(savedAppDeviceResponse.IsSuccessStatusCode);
                var savedAppDevice = savedAppDeviceResponse.Content;

                Assert.IsNotNull(savedAppDevice);
                Assert.IsTrue(savedAppDevice.ApplicationId == appId);
                Assert.IsFalse(savedAppDevice.IsRevoked);
                Assert.IsFalse(savedAppDevice.Id == Guid.Empty);

                //Assert.IsTrue(savedAppDevice.HalfAdek); ???
                Assert.IsTrue(Convert.ToBase64String(savedAppDevice.SharedSecretKey) == payload.SharedSecretKey64);
                Assert.IsTrue(Convert.ToBase64String(savedAppDevice.UniqueDeviceId) == payload.DeviceId64); //note: i suppose, this is kind of a hackish way to compare the byte arrays 
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