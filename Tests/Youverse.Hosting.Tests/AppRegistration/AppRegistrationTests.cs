using System;
using System.Reflection;
using NUnit.Framework;
using Refit;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.AppRegistration;
using Youverse.Hosting.Tests.ApiClient;

namespace Youverse.Hosting.Tests.AppRegistration
{
    public class AppRegistrationTests
    {
        private TestScaffold _scaffold;
        private Guid _applicationid = Guid.Parse(" f499fc5f-1111-1111-1111-bd44a13b7dbd ");
        private string _appName = "API Tests Sample App";

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
        public async void RegisterNewApp()
        {
            
            //Check if Frodo received the request?
            using (var client = _scaffold.CreateHttpClient(_scaffold.Frodo))
            {
                var svc = RestService.For<IAppRegistrationTestHttpClient>(client);
                var payload = new AppRegistrationPayload
                {
                    Name = _appName,
                    ApplicationId = _applicationid
                };
                
                var response = await svc.RegisterApp(payload);
                
                Assert.IsTrue(response.IsSuccessStatusCode);

                var savedApp = response.Content;
   
            }
            
            // var appRegSvc = new AppRegistrationService(null, null, null);
            //
            // Guid applicationId = Guid.Parse("9076c849-5217-464f-b294-cb3036fec64e");
            // string appName = "Youverse Chat";
            //
            // await appRegSvc.RegisterApplication(applicationId, appName);
            // var savedApp = await appRegSvc.GetRegistration(applicationId);
            //
            // Assert.IsNotNull(savedApp);

            //TODO: add more validation
        }

        [Test]
        public async void DeleteAppRegistration()
        {
            // var appRegSvc = new AppRegistrationService(null, null, null);
            //
            // Guid applicationId = Guid.Parse("9076c849-5217-464f-b294-cb3036fec64e");
            // string appName = "Youverse Chat";
            //
            // await appRegSvc.RegisterApplication(applicationId, appName);
            // var savedApp = await appRegSvc.GetRegistration(applicationId);
            //
            // Assert.IsNotNull(savedApp);
            //
            // appRegSvc.Delete(applicationId);
        }

        [Test]
        public async void RegisterAppOnDevice()
        {
            // var appRegSvc = new AppRegistrationService(null, null, null);
            //
            // Guid applicationId = Guid.Parse("9076c849-5217-464f-b294-cb3036fec64e");
            // string appName = "Youverse Chat";
            //
            // await appRegSvc.RegisterApplication(applicationId, appName);
            // var savedApp = await appRegSvc.GetRegistration(applicationId);
            // Assert.IsNotNull(savedApp);
            //
            // var uniqueDeviceId = Guid.Parse("a917c85f-732d-4991-a3d9-5aeba3e89f32").ToByteArray();
            // var sharedSecret = Guid.NewGuid().ToByteArray();
            // var reply = await appRegSvc.RegisterAppOnDevice(applicationId, uniqueDeviceId, sharedSecret);
            //
            // Assert.IsNotNull(reply);
            //
            // var savedReg = await appRegSvc.GetDeviceAppRegistration(reply.Id);
            //
            // Assert.IsNotNull(savedReg);
            // Assert.IsTrue(savedReg.SharedSecret == sharedSecret);
            
            //todo: test others
        }
    }
}