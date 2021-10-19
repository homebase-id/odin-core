using System;
using NUnit.Framework;
using Youverse.Core.Cryptography;
using Youverse.Core.Cryptography.Data;
using Youverse.Core.Services.Authorization.AppRegistration;

namespace Youverse.Hosting.Tests.AppRegistration
{
    public class AppRegistrationTests
    {
        [Test]
        public async void RegisterNewApp()
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