using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Transit;
using Youverse.Hosting.Controllers.OwnerToken.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Drive.Management;

public class DriveManagementTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [OneTimeTearDown]
    public void OneTimeTearDown()
    {
        _scaffold.RunAfterAnyTests();
    }

    [Test]
    public async Task CanCreateAndGetDrive()
    {
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.DotYouId, out var ownerSharedSecret))
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
            Assert.NotNull(page.Results.SingleOrDefault(drive => drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));
        }
    }

    [Test]
    public async Task CannotCreateDuplicateDriveByAliasAndType()
    {
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
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
            Assert.NotNull(page.Results.SingleOrDefault(drive => drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));

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
        using (var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.DotYouId, out var ownerSharedSecret))
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
            Assert.NotNull(page.Results.SingleOrDefault(drive => drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));

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
}