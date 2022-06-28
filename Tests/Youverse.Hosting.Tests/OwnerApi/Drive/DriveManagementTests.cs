using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Drive;
using Youverse.Hosting.Tests.OwnerApi.Scaffold;

namespace Youverse.Hosting.Tests.OwnerApi.Drive;

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
        using (var client = _scaffold.OwnerTestUtils.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
        {
            var svc = RestService.For<IDriveManagementHttpClient>(client);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";

            var response = await svc.CreateDrive(targetDrive, name, metadata, false);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(1, 100);
            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.IsTrue(page.Results.Any());
            Assert.NotNull(page.Results.SingleOrDefault(drive => drive.Alias == targetDrive.Alias && drive.Type == targetDrive.Type));
        }
    }

    [Test]
    public async Task CannotCreateDuplicateDriveByAliasAndType()
    {
        using (var client = _scaffold.OwnerTestUtils.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
        {
            var svc = RestService.For<IDriveManagementHttpClient>(client);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";

            var response = await svc.CreateDrive(targetDrive, name, metadata, false);

            Assert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            Assert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(1, 100);
            Assert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            Assert.IsTrue(page.Results.Any());
            Assert.NotNull(page.Results.SingleOrDefault(drive => drive.Alias == targetDrive.Alias && drive.Type == targetDrive.Type));
            
            var createDuplicateDriveResponse = await svc.CreateDrive(targetDrive, "drive 02", "some metadata", false);
            Assert.IsFalse(createDuplicateDriveResponse.IsSuccessStatusCode, $"Create drive with duplicate alias and type should have failed");
            
        }
    }
}