using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Hosting.Controllers.OwnerToken.Drive;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Management;

public class DriveManagementTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [SetUp]
    public void Setup()
    {
        _scaffold.ClearAssertLogEventsAction();
        _scaffold.ClearLogEvents();
    }

    [TearDown]
    public void TearDown()
    {
        _scaffold.AssertLogEvents();
    }


    [Test]
    public async Task CanCreateAndGetDrive()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = name,
                Metadata = metadata,
                AllowAnonymousReads = false,
                Attributes = new Dictionary<string, string>()
                {
                    { "some_attribute", "a_value" }
                }
            });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.IsTrue(page.Results.Any());
            var drive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            Assert.NotNull(drive);
            Assert.IsTrue(drive.Attributes["some_attribute"] == "a_value");
        }
    }

    [Test]
    public async Task CannotCreateDuplicateDriveByAliasAndType()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = name,
                Metadata = metadata,
                AllowAnonymousReads = false
            });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.IsTrue(page.Results.Any());
            Assert.NotNull(page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));

            var createDuplicateDriveResponse = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = "drive 02",
                Metadata = "some metadata",
                AllowAnonymousReads = false
            });
            Assert.IsFalse(createDuplicateDriveResponse.IsSuccessStatusCode, $"Create drive with duplicate alias and type should have failed");
        }
    }

    [Test]
    public async Task CanUpdateDriveMetadata()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = name,
                Metadata = metadata,
                AllowAnonymousReads = false
            });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.IsTrue(page.Results.Any());
            Assert.NotNull(page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));

            await svc.UpdateMetadata(new UpdateDriveDefinitionRequest()
            {
                TargetDrive = targetDrive,
                Metadata = "ankles and toes"
            });

            var getUpdatedResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrivesPage = getUpdatedResponse.Content;
            Assert.IsNotNull(updatedDrivesPage);

            var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            Assert.IsTrue(updatedDrive.Metadata == "ankles and toes");
        }
    }

    [Test]
    public async Task CanUpdateDriveAttributes()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = name,
                Metadata = metadata,
                AllowAnonymousReads = false,
                Attributes = new Dictionary<string, string>()
                {
                    { "a1", "a2" }
                }
            });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.IsTrue(page.Results.Any());
            var drive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            Assert.NotNull(drive);
            Assert.IsTrue(drive.Attributes["a1"] == "a2");

            await svc.UpdateAttributes(new UpdateDriveDefinitionRequest()
            {
                TargetDrive = targetDrive,
                Attributes = new Dictionary<string, string>()
                {
                    { "a1", "a3" },
                    { "b1", "z44" }
                }
            });

            var getUpdatedResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrivesPage = getUpdatedResponse.Content;
            Assert.IsNotNull(updatedDrivesPage);

            var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            Assert.IsTrue(updatedDrive.Attributes["a1"] == "a3");
            Assert.IsTrue(updatedDrive.Attributes["b1"] == "z44");
        }
    }

    [Test]
    public async Task CanSetSystemDriveReadMode()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = name,
                Metadata = metadata,
                AllowAnonymousReads = false
            });

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.IsTrue(page.Results.Any());
            var theDrive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            Assert.NotNull(theDrive);
            Assert.IsFalse(theDrive.AllowAnonymousReads);

            var setDriveModeResponse = await svc.SetDriveReadMode(new UpdateDriveReadModeRequest()
            {
                TargetDrive = targetDrive,
                AllowAnonymousReads = true
            });

            Assert.IsTrue(setDriveModeResponse.IsSuccessStatusCode);

            var getUpdatedResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            Assert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrivesPage = getUpdatedResponse.Content;
            Assert.IsNotNull(updatedDrivesPage);

            var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            Assert.IsTrue(updatedDrive.AllowAnonymousReads);
        }
    }

    [Test]
    public async Task FailToSetSystemDriveReadMode()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            foreach (var systemDrive in SystemDriveConstants.SystemDrives)
            {
                var response = await svc.SetDriveReadMode(new UpdateDriveReadModeRequest()
                {
                    TargetDrive = systemDrive,
                    AllowAnonymousReads = true
                });

                Assert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, "Should have failed to set system drive read-mode");
            }
        }
    }
}