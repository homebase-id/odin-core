using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.JsonPatch.Internal;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Hosting.Controllers.Apps;
using Youverse.Hosting.Tests.ApiClient;

namespace Youverse.Hosting.Tests.Apps
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
            var newId = await AddSampleApp(appId, name);
        }

        [Test]
        public async Task RevokeAppRegistration()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-revoke";

            var newId = AddSampleApp(appId, name);

            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<IAppRegistrationTestHttpClient>(client);
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

            var newId = AddSampleApp(appId, name);

            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<IAppRegistrationTestHttpClient>(client);

                var payload = new AppDeviceRegistrationPayload()
                {
                    ApplicationId = appId,
                    DeviceId64 = Convert.ToBase64String(Guid.Parse("a917c85f-732d-4991-a3d9-5aeba3e89f32").ToByteArray()),
                    SharedSecret64 = Convert.ToBase64String(Guid.NewGuid().ToByteArray())
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
                Assert.IsTrue(Convert.ToBase64String(savedAppDevice.SharedSecret) == payload.DeviceId64);
                Assert.IsTrue(Convert.ToBase64String(savedAppDevice.UniqueDeviceId) == payload.DeviceId64); //note: i suppose, this is kind of a hackish way to compare the byte arrays 
            }
        }

        private async Task<Guid> AddSampleApp(Guid applicationId, string name)
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<IAppRegistrationTestHttpClient>(client);
                var payload = new AppRegistrationPayload
                {
                    Name = name,
                    ApplicationId = applicationId
                };

                var response = await svc.RegisterApp(payload);

                Assert.IsTrue(response.IsSuccessStatusCode);
                var newId = response.Content;
                Assert.IsTrue(newId != Guid.Empty);

                var savedApp = await GetSampleApp(applicationId);
                Assert.IsTrue(savedApp.Id == newId);
                Assert.IsTrue(savedApp.ApplicationId == payload.ApplicationId);
                Assert.IsTrue(savedApp.Name == payload.Name);

                return newId;
            }
        }

        private async Task<AppRegistration> GetSampleApp(Guid applicationId)
        {
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<IAppRegistrationTestHttpClient>(client);
                var appResponse = await svc.GetRegisteredApp(applicationId);
                Assert.IsTrue(appResponse.IsSuccessStatusCode, $"Could not retrieve the app {applicationId}");
                Assert.IsNotNull(appResponse.Content, $"Could not retrieve the app {applicationId}");
                var savedApp = appResponse.Content;
                return savedApp;
            }
        }
    }
}