using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NUnit.Framework;
using Youverse.Core.Cryptography;
using Youverse.Core.Services.Authorization.Apps;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Tests.AppReg
{
    [TestFixture]
    public class AppRegistrationTests
    {
        private ServiceTestScaffold _scaffold;
        private byte[] _deviceSharedSecret = new byte[16];

        [SetUp]
        public void Setup()
        {
            string folder = MethodBase.GetCurrentMethod().DeclaringType.Name;
            _scaffold = new ServiceTestScaffold(folder);
            _scaffold.CreateContext();
            _scaffold.CreateSystemStorage();
            _scaffold.CreateLoggerFactory();
        }

        [TearDown]
        public void Cleanup()
        {
            _scaffold.Cleanup();
        }

        [Test]
        public async Task RegisterAppWithDrive()
        {
            var logger = Substitute.For<ILogger<AppRegistrationService>>();
            var driveService = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            var appRegSvc = new AppRegistrationService(_scaffold.Context, logger, _scaffold.SystemStorage, driveService);

            Guid appId = Guid.NewGuid();
            string appName = "Test_App";
            await appRegSvc.RegisterApp(appId, appName, true);

            var storedApp = await appRegSvc.GetAppRegistration(appId);

            //Assert.IsTrue(storedApp.Id == newId);
            Assert.IsTrue(storedApp.ApplicationId == appId);
            Assert.IsTrue(storedApp.Name == appName);
            Assert.IsNotNull(storedApp.DriveId);

            var storedDrive = await driveService.GetDrive(storedApp.DriveId.GetValueOrDefault());
            Assert.IsNotNull(storedDrive);
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

            var newId = AddSampleAppNoDrive(appId, name);

            var svc = CreateAppRegService();
            await svc.RevokeApp(appId);

            var savedApp = await GetSampleApp(appId);
            Assert.IsNotNull(savedApp);
            Assert.IsTrue(savedApp.IsRevoked);
        }

        [Test]
        public async Task RegisterAppOnDevice()
        {
            var appId = Guid.NewGuid();
            var name = "API Tests Sample App-reg-app-device";

            var uniqueDeviceId = Guid.Parse("a917c85f-732d-4991-a3d9-5aeba3e89f32").ToByteArray();
            var sharedSecret = Guid.NewGuid().ToByteArray();


            var appReg = AddSampleAppNoDrive(appId, name);

            //TODO: rsa encrypt the shared secret

            var svc = CreateAppRegService();

            var reply = await svc.RegisterAppOnDevice(appId, uniqueDeviceId, sharedSecret);

            Assert.IsFalse(reply.Token == Guid.Empty);
            Assert.IsNotNull(reply.DeviceAppKey);
            Assert.That(reply.DeviceAppKey.Length, Is.GreaterThanOrEqualTo(16));

            var savedAppDevice = await svc.GetAppDeviceRegistration(appId, uniqueDeviceId);

            Assert.IsNotNull(savedAppDevice);
            Assert.IsTrue(savedAppDevice.ApplicationId == appId);
            Assert.IsFalse(savedAppDevice.IsRevoked);
            Assert.IsFalse(savedAppDevice.Id == Guid.Empty);

            //Assert.IsTrue(savedAppDevice.HalfAdek); ???
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(savedAppDevice.SharedSecret, sharedSecret));
            Assert.IsTrue(ByteArrayUtil.EquiByteArrayCompare(savedAppDevice.UniqueDeviceId, uniqueDeviceId));
        }

        private async Task<AppRegistrationResponse> AddSampleAppNoDrive(Guid applicationId, string name)
        {
            var svc = CreateAppRegService();
            var appReg = await svc.RegisterApp(applicationId, name, false);

            Assert.IsTrue(appReg.ApplicationId == applicationId);
            Assert.IsTrue(appReg.Name == name);
            Assert.IsTrue(appReg.DriveId == null);


            var savedApp = await GetSampleApp(applicationId);
            Assert.IsTrue(savedApp.ApplicationId == applicationId);
            Assert.IsTrue(savedApp.Name == name);
            Assert.IsTrue(appReg.DriveId == null);

            return appReg;
        }

        private async Task<AppRegistrationResponse> GetSampleApp(Guid applicationId)
        {
            var svc = CreateAppRegService();
            var savedApp = await svc.GetAppRegistration(applicationId);
            return savedApp;
        }

        private AppRegistrationService CreateAppRegService()
        {
            var logger = Substitute.For<ILogger<AppRegistrationService>>();
            var driveService = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            var appRegSvc = new AppRegistrationService(_scaffold.Context, logger, _scaffold.SystemStorage, driveService);
            return appRegSvc;
        }
    }
}