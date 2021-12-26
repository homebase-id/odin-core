using System;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Tests.Provisioning
{
    [TestFixture]
    public class DriveManagerTests
    {
        private ServiceTestScaffold _scaffold;

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
        public async Task CanCreateDrive()
        {
            var driveManager = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);

            const string driveName = "Test-Drive";
            var storageDrive = await driveManager.CreateDrive(driveName);
            Assert.IsNotNull(storageDrive);
            Assert.That(storageDrive.Name, Is.EqualTo(driveName));

            var retrievedStorageDrive = await driveManager.GetDrive(storageDrive.Id);

            Assert.AreEqual(storageDrive.Id, retrievedStorageDrive.Id);
            Assert.AreEqual(storageDrive.Name, retrievedStorageDrive.Name);
        }

        [Test]
        public async Task WillFailIfInvalidDriveRequested()
        {
            var driveManager = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            Assert.ThrowsAsync<InvalidDriveException>(async () => await driveManager.GetDrive(Guid.NewGuid(), failIfInvalid: true));
        }
        
        [Test]
        public async Task NullReturnedForInvalidDrive()
        {
            var driveManager = new DriveService(_scaffold.Context, _scaffold.SystemStorage, _scaffold.LoggerFactory);
            var drive =  await driveManager.GetDrive(Guid.NewGuid(), failIfInvalid: false);
            Assert.IsNull(drive);
        }
    }
}