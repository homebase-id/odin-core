using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Tests.OwnerApi.Drive;

public class DriveManagementTests
{
    private TestScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        string folder = MethodBase.GetCurrentMethod()!.DeclaringType!.Name;
        _scaffold = new TestScaffold(folder);
        _scaffold.RunBeforeAnyTests();
    }

    [Test]
    public async Task CanCreateDrive()
    {
        using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
        {
            var svc = RestService.For<IDriveManagementHttpClient>(client);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";
            bool allowAnonymousReads = false;

            var response = await svc.CreateDrive(targetDrive, name, metadata, allowAnonymousReads);

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
        using (var client = _scaffold.CreateOwnerApiHttpClient(TestIdentities.Frodo, out var ownerSharedSecret))
        {
            var svc = RestService.For<IDriveManagementHttpClient>(client);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();
            string name = "test drive 01";
            string metadata = "{some:'json'}";
            bool allowAnonymousReads = false;

            var response = await svc.CreateDrive(targetDrive, name, metadata, allowAnonymousReads);

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