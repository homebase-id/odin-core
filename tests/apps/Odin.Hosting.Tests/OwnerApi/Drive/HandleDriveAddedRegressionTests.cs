using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Odin.Hosting.Tests._Universal.DriveTests;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.OwnerApi.Drive;

/// <summary>
/// Regression: <c>DriveManager.CreateDriveAsync</c> on a tenant that has not yet completed
/// <c>EnsureInitialOwnerSetupAsync</c> used to 500. The drive insert succeeded but the MediatR
/// notification handler <c>CircleNetworkService.HandleDriveAdded</c> NRE'd on the missing
/// <c>ConfirmedConnectionsCircle</c>. The fix is a null-guard with a warning log — the drive is
/// still inserted, just without an anonymous-read grant on the not-yet-created system circles.
/// </summary>
public class HandleDriveAddedRegressionTests
{
    private WebScaffold _scaffold;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        var folder = GetType().Name;
        _scaffold = new WebScaffold(folder);
        _scaffold.RunBeforeAnyTests(initializeIdentity: false,
            testIdentities: [TestIdentities.Frodo]);
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

    [Test]
    public async Task CanCreateAnonymousReadableDriveBeforeInitializeIdentity()
    {
        var ownerApiClient = _scaffold.CreateOwnerApiClientRedux(TestIdentities.Frodo);
        var drive = TargetDrive.NewTargetDrive();

        // Tenant has NOT been initialized (RunBeforeAnyTests with initializeIdentity: false),
        // so system circles don't exist. Creating an anonymous-readable drive should still
        // return 200 — HandleDriveAdded null-guards the missing circles.
        var response = await ownerApiClient.DriveManager.CreateDrive(
            drive, "regression-test-drive", "", allowAnonymousReads: true);

        ClassicAssert.IsTrue(response.IsSuccessStatusCode,
            $"CreateDrive returned {response.StatusCode}; expected 200. " +
            "HandleDriveAdded null-guard is the regression fix being validated.");

        // The warning logs surface what would otherwise be silent. We don't run AssertLogEvents
        // in [TearDown] strictly — we expect the framework to be tolerant of warnings here.
        _scaffold.ClearAssertLogEventsAction();
    }
}
