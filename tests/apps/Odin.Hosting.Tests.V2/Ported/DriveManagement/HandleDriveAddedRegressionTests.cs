using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.Tests.V2.Ported.DriveManagement;

/// <summary>
/// Port of <c>OwnerApi/Drive/HandleDriveAddedRegressionTests</c>.
///
/// Regression: <c>DriveManager.CreateDriveAsync</c> on a tenant that has not yet completed
/// <c>EnsureInitialOwnerSetupAsync</c> used to 500. The drive insert succeeded but the MediatR
/// notification handler <c>CircleNetworkService.HandleDriveAdded</c> NRE'd on the missing
/// <c>ConfirmedConnectionsCircle</c>. The fix is a null-guard with a warning log — the drive is
/// still inserted, just without an anonymous-read grant on the not-yet-created system circles.
/// </summary>
/// <remarks>
/// The original got its un-initialized tenant from <c>RunBeforeAnyTests(initializeIdentity: false)</c>.
/// Here that is <see cref="WarmTenantBaselineAsync"/> overridden to log in (which sets the owner
/// password — required before <c>OdinHost.TakeBaselineAsync</c> snapshots the identity DB) but
/// deliberately <i>not</i> call <c>Admin.InitializeIdentity()</c>. Without that call the system
/// circles and system drives do not exist, which is exactly the state the regression needs. Verified:
/// the fixture boots and the test exercises the un-initialized path.
///
/// No caller matrix — an <c>OwnerApi</c> fixture, so a plain <c>[Test]</c> and <c>LoginAsOwner</c>;
/// the <c>SetupCallerWithOwner</c> ordering caveat does not apply. <c>CreateDrive</c> is the system
/// under test, so it goes through <see cref="IRefitDriveManagement"/> via
/// <see cref="OwnerSession.RefitFor{T}"/> rather than <c>owner.Admin</c>, which throws on non-2xx
/// and would turn the assertion into an exception.
///
/// The original's trailing <c>_scaffold.ClearAssertLogEventsAction()</c> — and its comment about
/// tolerating the warning logs the null-guard emits — has no counterpart: the fast framework
/// captures no log events at all, so nothing here can fail on a logged warning.
/// </remarks>
[TestFixture]
public class HandleDriveAddedRegressionTests : V2Fixture
{
    /// <summary>
    /// Logs each identity's owner in (to set the password the snapshot baseline needs) and stops
    /// there. Overriding away <c>InitializeIdentity</c> is the whole point of this fixture: the
    /// regression only reproduces on a tenant whose system circles have not been created.
    /// </summary>
    protected override async Task WarmTenantBaselineAsync()
    {
        foreach (var identity in HostIdentities)
        {
            await LoginAsOwner(identity);
        }
    }

    [Test]
    public async Task CanCreateAnonymousReadableDriveBeforeInitializeIdentity()
    {
        var ownerApiClient = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();

        // Tenant has NOT been initialized (WarmTenantBaselineAsync skips InitializeIdentity),
        // so system circles don't exist. Creating an anonymous-readable drive should still
        // return 200 — HandleDriveAdded null-guards the missing circles.
        var response = await ownerApiClient.RefitFor<IRefitDriveManagement>().CreateDrive(new CreateDriveRequest
        {
            TargetDrive = drive,
            Name = "regression-test-drive",
            Metadata = "",
            AllowAnonymousReads = true
        });

        Assert.That(response.IsSuccessStatusCode, Is.True,
            $"CreateDrive returned {response.StatusCode}; expected 200. " +
            "HandleDriveAdded null-guard is the regression fix being validated.");
    }
}
