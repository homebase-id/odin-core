using System;
using NUnit.Framework;
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
        
       
    }
}