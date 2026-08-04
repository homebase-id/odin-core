using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using SQLitePCL;

namespace Odin.Hosting.Tests.OwnerApi.Drive.Management;

public class DriveManagementTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(testIdentities: new List<TestIdentity>() { TestIdentities.Frodo });
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

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            ClassicAssert.IsNotNull(response.Content);

            // Cache miss
            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            ClassicAssert.IsTrue(page.Results.Any());
            var drive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            ClassicAssert.NotNull(drive);
            ClassicAssert.IsTrue(drive.Attributes["some_attribute"] == "a_value");

            // Cache hit
            getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);

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

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            ClassicAssert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            ClassicAssert.IsTrue(page.Results.Any());
            ClassicAssert.NotNull(page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));

            var createDuplicateDriveResponse = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = "drive 02",
                Metadata = "some metadata",
                AllowAnonymousReads = false
            });
            ClassicAssert.IsFalse(createDuplicateDriveResponse.IsSuccessStatusCode,
                $"Create drive with duplicate alias and type should have failed");
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

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            ClassicAssert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            ClassicAssert.IsTrue(page.Results.Any());
            ClassicAssert.NotNull(page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type));

            await svc.UpdateMetadata(new UpdateDriveDefinitionRequest()
            {
                TargetDrive = targetDrive,
                Metadata = "ankles and toes"
            });

            var getUpdatedResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrivesPage = getUpdatedResponse.Content;
            ClassicAssert.IsNotNull(updatedDrivesPage);

            var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsTrue(updatedDrive.Metadata == "ankles and toes");
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

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            ClassicAssert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            ClassicAssert.IsTrue(page.Results.Any());
            var drive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            ClassicAssert.NotNull(drive);
            ClassicAssert.IsTrue(drive.Attributes["a1"] == "a2");

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
            ClassicAssert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrivesPage = getUpdatedResponse.Content;
            ClassicAssert.IsNotNull(updatedDrivesPage);

            var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsTrue(updatedDrive.Attributes["a1"] == "a3");
            ClassicAssert.IsTrue(updatedDrive.Attributes["b1"] == "z44");
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

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            ClassicAssert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            ClassicAssert.IsTrue(page.Results.Any());
            var theDrive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            ClassicAssert.NotNull(theDrive);
            ClassicAssert.IsFalse(theDrive.AllowAnonymousReads);

            var setDriveModeResponse = await svc.SetDriveReadMode(new UpdateDriveReadModeRequest()
            {
                TargetDrive = targetDrive,
                AllowAnonymousReads = true
            });

            ClassicAssert.IsTrue(setDriveModeResponse.IsSuccessStatusCode);

            var getUpdatedResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrivesPage = getUpdatedResponse.Content;
            ClassicAssert.IsNotNull(updatedDrivesPage);

            var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsTrue(updatedDrive.AllowAnonymousReads);
        }
    }

    [Test]
    public async Task CanSetSystemDriveAllowSubscriptionsFlag()
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
                AllowSubscriptions = false
            });

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");
            ClassicAssert.IsNotNull(response.Content);

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);
            var page = getDrivesResponse.Content;

            ClassicAssert.IsTrue(page.Results.Any());
            var theDrive = page.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            ClassicAssert.NotNull(theDrive);
            ClassicAssert.IsFalse(theDrive.AllowSubscriptions);

            var setDriveModeResponse = await svc.SetAllowSubscriptions(new UpdateDriveAllowSubscriptionsRequest()
            {
                TargetDrive = targetDrive,
                AllowSubscriptions = true
            });

            ClassicAssert.IsTrue(setDriveModeResponse.IsSuccessStatusCode);

            var getUpdatedResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrivesPage = getUpdatedResponse.Content;
            ClassicAssert.IsNotNull(updatedDrivesPage);

            var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsTrue(updatedDrive.AllowSubscriptions);
        }
    }

    [Test]
    public async Task NewDriveIsCdnEnabledByDefault_MatchingTheBehaviourAllowCdnReplaced()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();

            // Note AllowCdn is not set: before the flag existed a new drive was CDN-eligible
            // unless someone set blockcdn on it, and an omitting caller must still get that.
            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = "cdn test drive",
                Metadata = "{some:'json'}"
            });

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getDrivesResponse.IsSuccessStatusCode);

            var theDrive = getDrivesResponse.Content.Results.SingleOrDefault(drive =>
                drive.TargetDriveInfo.Alias == targetDrive.Alias && drive.TargetDriveInfo.Type == targetDrive.Type);
            ClassicAssert.NotNull(theDrive);
            ClassicAssert.IsTrue(theDrive.AllowCdn, "a new drive must stay CDN-enabled by default");

            // The owner can now turn it off - which the old attribute could not actually do on
            // an anonymous drive, because the old rule OR-ed blockcdn against AllowAnonymousReads
            var setResponse = await svc.SetAllowCdn(new UpdateDriveAllowCdnRequest()
            {
                TargetDrive = targetDrive,
                AllowCdn = false
            });
            ClassicAssert.IsTrue(setResponse.IsSuccessStatusCode);

            var getUpdatedResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            ClassicAssert.IsTrue(getUpdatedResponse.IsSuccessStatusCode);
            var updatedDrive = getUpdatedResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsFalse(updatedDrive.AllowCdn);

            // ...and back on again
            var resetResponse = await svc.SetAllowCdn(new UpdateDriveAllowCdnRequest()
            {
                TargetDrive = targetDrive,
                AllowCdn = true
            });
            ClassicAssert.IsTrue(resetResponse.IsSuccessStatusCode);

            var getFinalResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            var finalDrive = getFinalResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsTrue(finalDrive.AllowCdn);
        }
    }

    [Test]
    public async Task CanCreateDriveWithCdnExplicitlyDisabled()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();

            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = "cdn off at creation",
                Metadata = "{some:'json'}",
                AllowCdn = false
            });

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            var theDrive = getDrivesResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsFalse(theDrive.AllowCdn);
        }
    }

    [Test]
    public async Task OwnerOnlyDriveStaysCdnEligible_AsItWasBeforeAllowCdn()
    {
        var client = _scaffold.OldOwnerApi.CreateOwnerApiHttpClient(TestIdentities.Frodo.OdinId, out var ownerSharedSecret);
        {
            var svc = RefitCreator.RestServiceFor<IDriveManagementHttpClient>(client, ownerSharedSecret);

            TargetDrive targetDrive = TargetDrive.NewTargetDrive();

            // The old rule listed owner-only drives for the CDN too (the Contacts system drive is
            // one). Introducing the flag must not start rejecting or excluding them; whether that
            // *should* change is a separate decision.
            var response = await svc.CreateDrive(new CreateDriveRequest()
            {
                TargetDrive = targetDrive,
                Name = "owner only + cdn",
                Metadata = "{some:'json'}",
                OwnerOnly = true
            });

            ClassicAssert.IsTrue(response.IsSuccessStatusCode, $"Failed status code.  Value was {response.StatusCode}");

            var getDrivesResponse = await svc.GetDrives(new GetDrivesRequest() { PageNumber = 1, PageSize = 100 });
            var theDrive = getDrivesResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
            ClassicAssert.IsTrue(theDrive.AllowCdn);
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

                ClassicAssert.IsTrue(response.StatusCode == HttpStatusCode.Forbidden, "Should have failed to set system drive read-mode");
            }
        }
    }
}