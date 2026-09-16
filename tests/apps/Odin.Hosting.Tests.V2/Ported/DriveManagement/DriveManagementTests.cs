using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Refit;
using Odin.Core;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.Ported.DriveManagement;

/// <summary>
/// Port of <c>OwnerApi/Drive/Management/DriveManagementTests</c>.
///
/// The owner console's drive-management surface: creating a drive, reading it back, and the flags an
/// owner can flip on it afterwards (metadata, attributes, anonymous reads, subscriptions, CDN).
/// </summary>
/// <remarks>
/// Every call here is the system under test — creating and reading drives is what the fixture asserts
/// on — so nothing goes through <see cref="OwnerAdmin"/>, whose helpers pick their own request shape
/// and throw on non-2xx. The whole fixture drives <see cref="IRefitDriveManagement"/> via
/// <see cref="OwnerSession.RefitFor{T}"/>, which keeps each request body verbatim from the original.
///
/// Two names carried over as they were: <c>CanSetSystemDriveReadMode</c> and
/// <c>CanSetSystemDriveAllowSubscriptionsFlag</c> both operate on a freshly-created ordinary drive,
/// not a system drive. The behaviour they assert is real and unchanged by the port; only the names
/// are wrong. <c>FailToSetSystemDriveReadMode</c> is the one that genuinely uses
/// <see cref="BuiltinDrives.Protected"/>.
///
/// <c>SetupCallerWithOwner</c> is not used (no caller matrix — an <c>OwnerApi</c> fixture), so the
/// create-then-build ordering caveat in the porting rules does not apply.
/// </remarks>
[TestFixture]
public class DriveManagementTests : V2Fixture
{
    [Test]
    public async Task CanCreateAndGetDrive()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();
        const string name = "test drive 01";
        const string metadata = "{some:'json'}";

        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = name,
            Metadata = metadata,
            AllowAnonymousReads = false,
            Attributes = new Dictionary<string, string>
            {
                { "some_attribute", "a_value" }
            }
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        // Cache miss
        var getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);
        var page = getDrivesResponse.Content;

        Assert.That(page.Results, Is.Not.Empty);
        var drive = page.Results.SingleOrDefault(d =>
            d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type);
        Assert.That(drive, Is.Not.Null);
        Assert.That(drive.Attributes["some_attribute"], Is.EqualTo("a_value"));

        // Cache hit
        getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);
    }

    [Test]
    public async Task CannotCreateDuplicateDriveByAliasAndType()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();
        const string name = "test drive 01";
        const string metadata = "{some:'json'}";

        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = name,
            Metadata = metadata,
            AllowAnonymousReads = false
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);
        var page = getDrivesResponse.Content;

        Assert.That(page.Results, Is.Not.Empty);
        Assert.That(page.Results.SingleOrDefault(d =>
            d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type), Is.Not.Null);

        var createDuplicateDriveResponse = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = "drive 02",
            Metadata = "some metadata",
            AllowAnonymousReads = false
        });

        Assert.That(createDuplicateDriveResponse.IsSuccessStatusCode, Is.False,
            "Create drive with duplicate alias and type should have failed");
    }

    [Test]
    public async Task CanUpdateDriveMetadata()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();
        const string name = "test drive 01";
        const string metadata = "{some:'json'}";

        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = name,
            Metadata = metadata,
            AllowAnonymousReads = false
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);
        var page = getDrivesResponse.Content;

        Assert.That(page.Results, Is.Not.Empty);
        Assert.That(page.Results.SingleOrDefault(d =>
            d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type), Is.Not.Null);

        await svc.UpdateMetadata(new UpdateDriveDefinitionRequest
        {
            TargetDrive = targetDrive,
            Metadata = "ankles and toes"
        });

        var getUpdatedResponse = await GetDrives(svc);
        Assert.That(getUpdatedResponse.IsSuccessStatusCode, Is.True);
        var updatedDrivesPage = getUpdatedResponse.Content;
        Assert.That(updatedDrivesPage, Is.Not.Null);

        var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(updatedDrive.Metadata, Is.EqualTo("ankles and toes"));
    }

    [Test]
    public async Task CanUpdateDriveAttributes()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();
        const string name = "test drive 01";
        const string metadata = "{some:'json'}";

        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = name,
            Metadata = metadata,
            AllowAnonymousReads = false,
            Attributes = new Dictionary<string, string>
            {
                { "a1", "a2" }
            }
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);
        var page = getDrivesResponse.Content;

        Assert.That(page.Results, Is.Not.Empty);
        var drive = page.Results.SingleOrDefault(d =>
            d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type);
        Assert.That(drive, Is.Not.Null);
        Assert.That(drive.Attributes["a1"], Is.EqualTo("a2"));

        await svc.UpdateAttributes(new UpdateDriveDefinitionRequest
        {
            TargetDrive = targetDrive,
            Attributes = new Dictionary<string, string>
            {
                { "a1", "a3" },
                { "b1", "z44" }
            }
        });

        var getUpdatedResponse = await GetDrives(svc);
        Assert.That(getUpdatedResponse.IsSuccessStatusCode, Is.True);
        var updatedDrivesPage = getUpdatedResponse.Content;
        Assert.That(updatedDrivesPage, Is.Not.Null);

        var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(updatedDrive.Attributes["a1"], Is.EqualTo("a3"));
        Assert.That(updatedDrive.Attributes["b1"], Is.EqualTo("z44"));
    }

    [Test]
    public async Task CanSetSystemDriveReadMode()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();
        const string name = "test drive 01";
        const string metadata = "{some:'json'}";

        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = name,
            Metadata = metadata,
            AllowAnonymousReads = false
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);
        var page = getDrivesResponse.Content;

        Assert.That(page.Results, Is.Not.Empty);
        var theDrive = page.Results.SingleOrDefault(d =>
            d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type);
        Assert.That(theDrive, Is.Not.Null);
        Assert.That(theDrive.AllowAnonymousReads, Is.False);

        var setDriveModeResponse = await svc.SetDriveReadMode(new UpdateDriveReadModeRequest
        {
            TargetDrive = targetDrive,
            AllowAnonymousReads = true
        });

        Assert.That(setDriveModeResponse.IsSuccessStatusCode, Is.True);

        var getUpdatedResponse = await GetDrives(svc);
        Assert.That(getUpdatedResponse.IsSuccessStatusCode, Is.True);
        var updatedDrivesPage = getUpdatedResponse.Content;
        Assert.That(updatedDrivesPage, Is.Not.Null);

        var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(updatedDrive.AllowAnonymousReads, Is.True);
    }

    [Test]
    public async Task CanSetSystemDriveAllowSubscriptionsFlag()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();
        const string name = "test drive 01";
        const string metadata = "{some:'json'}";

        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = name,
            Metadata = metadata,
            AllowSubscriptions = false
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");
        Assert.That(response.Content, Is.Not.Null);

        var getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);
        var page = getDrivesResponse.Content;

        Assert.That(page.Results, Is.Not.Empty);
        var theDrive = page.Results.SingleOrDefault(d =>
            d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type);
        Assert.That(theDrive, Is.Not.Null);
        Assert.That(theDrive.AllowSubscriptions, Is.False);

        var setDriveModeResponse = await svc.SetAllowSubscriptions(new UpdateDriveAllowSubscriptionsRequest
        {
            TargetDrive = targetDrive,
            AllowSubscriptions = true
        });

        Assert.That(setDriveModeResponse.IsSuccessStatusCode, Is.True);

        var getUpdatedResponse = await GetDrives(svc);
        Assert.That(getUpdatedResponse.IsSuccessStatusCode, Is.True);
        var updatedDrivesPage = getUpdatedResponse.Content;
        Assert.That(updatedDrivesPage, Is.Not.Null);

        var updatedDrive = updatedDrivesPage.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(updatedDrive.AllowSubscriptions, Is.True);
    }

    [Test]
    public async Task NewDriveIsCdnDisabledByDefault()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();

        // AllowCdn is not set. It is opt-in, so an omitting caller gets a drive the CDN
        // cannot read - the point of retiring the blockcdn attribute.
        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = "cdn test drive",
            Metadata = "{some:'json'}"
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");

        var getDrivesResponse = await GetDrives(svc);
        Assert.That(getDrivesResponse.IsSuccessStatusCode, Is.True);

        var theDrive = getDrivesResponse.Content.Results.SingleOrDefault(d =>
            d.TargetDriveInfo.Alias == targetDrive.Alias && d.TargetDriveInfo.Type == targetDrive.Type);
        Assert.That(theDrive, Is.Not.Null);
        Assert.That(theDrive.AllowCdn, Is.False, "a new drive must default to CDN-disabled");

        // The owner opts in...
        var setResponse = await svc.SetAllowCdn(new UpdateDriveAllowCdnRequest
        {
            TargetDrive = targetDrive,
            AllowCdn = true
        });
        Assert.That(setResponse.IsSuccessStatusCode, Is.True);

        var getUpdatedResponse = await GetDrives(svc);
        Assert.That(getUpdatedResponse.IsSuccessStatusCode, Is.True);
        var updatedDrive = getUpdatedResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(updatedDrive.AllowCdn, Is.True);

        // ...and back off again
        var resetResponse = await svc.SetAllowCdn(new UpdateDriveAllowCdnRequest
        {
            TargetDrive = targetDrive,
            AllowCdn = false
        });
        Assert.That(resetResponse.IsSuccessStatusCode, Is.True);

        var getFinalResponse = await GetDrives(svc);
        var finalDrive = getFinalResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(finalDrive.AllowCdn, Is.False);
    }

    [Test]
    public async Task CanCreateDriveWithCdnExplicitlyEnabled()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();

        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = "cdn on at creation",
            Metadata = "{some:'json'}",
            AllowCdn = true
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");

        var getDrivesResponse = await GetDrives(svc);
        var theDrive = getDrivesResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(theDrive.AllowCdn, Is.True);
    }

    [Test]
    public async Task OwnerOnlyDriveIsCdnDisabledByDefaultButCanBeEnabled()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        var targetDrive = TargetDrive.NewTargetDrive();

        // There is deliberately no owner-only guard, so this must not be rejected - but it
        // must not be CDN-enabled implicitly either. Off unless the owner asks.
        var response = await svc.CreateDrive(new CreateDriveRequest
        {
            TargetDrive = targetDrive,
            Name = "owner only + cdn",
            Metadata = "{some:'json'}",
            OwnerOnly = true
        });

        Assert.That(response.IsSuccessStatusCode, Is.True, $"Failed status code.  Value was {response.StatusCode}");

        var getDrivesResponse = await GetDrives(svc);
        var theDrive = getDrivesResponse.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(theDrive.AllowCdn, Is.False, "owner-only drive must not be CDN-enabled implicitly");

        var setResponse = await svc.SetAllowCdn(new UpdateDriveAllowCdnRequest
        {
            TargetDrive = targetDrive,
            AllowCdn = true
        });
        Assert.That(setResponse.IsSuccessStatusCode, Is.True, "no owner-only guard: enabling must be allowed");

        var getUpdated = await GetDrives(svc);
        var updated = getUpdated.Content.Results.Single(dr => dr.TargetDriveInfo == targetDrive);
        Assert.That(updated.AllowCdn, Is.True);
    }

    [Test]
    public async Task FailToSetSystemDriveReadMode()
    {
        var owner = await LoginAsOwner();
        var svc = owner.RefitFor<IRefitDriveManagement>();

        foreach (var systemDrive in BuiltinDrives.Protected)
        {
            var response = await svc.SetDriveReadMode(new UpdateDriveReadModeRequest
            {
                TargetDrive = systemDrive,
                AllowAnonymousReads = true
            });

            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden),
                $"Should have failed to set system drive read-mode for {systemDrive}");
        }
    }

    /// <summary>
    /// The page shape the original fixture read drives back with — page 1, 100 per page — kept as-is
    /// rather than routed through <c>owner.Admin.GetDrives</c>, whose own paging differs.
    /// </summary>
    private static Task<ApiResponse<PagedResult<OwnerClientDriveData>>> GetDrives(IRefitDriveManagement svc) =>
        svc.GetDrives(new GetDrivesRequest { PageNumber = 1, PageSize = 100 });
}
