using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps.Builtin;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.DriveManagement;

/// <summary>
/// Port of <c>OwnerApi/Drive/Management/DriveManagementArchivalTests</c>.
///
/// Archiving is a visibility switch, not a delete: an archived drive keeps showing up in the
/// owner console's drive list and disappears from every other caller's. Un-archiving brings it
/// back for everyone, and the protected system drives refuse the flag outright.
/// </summary>
/// <remarks>
/// The two archival cases carry the original's <c>[Explicit]</c> verbatim, so they do not run under
/// plain <c>dotnet test</c> — a port is a move, not a rewrite. All six rows do pass here when named
/// explicitly on the command line; whether the marker is still warranted is a separate decision from
/// the port.
///
/// <para>
/// <b>Ordering verdict (the <c>SetupCallerWithOwner</c> bite).</b> The original created both drives
/// before <c>callerContext.Initialize</c>; here the spec's drive and the caller are built in one
/// step, so "drive 2" is created afterwards. Inert: the list endpoint reads the live drive set out
/// of <c>DriveManager.GetDrivesAsync(type, …)</c>, which filters on the caller being anonymous and
/// on <c>IsArchived</c> versus the caller's master key — never on the caller's drive grants. Neither
/// drive is in the App/Guest grant in either ordering, and both are expected in the list regardless.
/// </para>
///
/// <para>
/// The archive flag itself is set through <c>owner.Admin</c> (arrange: the drive under observation
/// has to end up archived), while <see cref="FailToArchiveSystemDrive"/> asserts a refusal and so
/// goes through <see cref="OwnerSession.RefitFor{T}"/>.
/// </para>
/// </remarks>
[TestFixture]
public class DriveManagementArchivalTests : V2Fixture
{
    /// <summary>
    /// Only the owner console sees an archived drive; an app or guest holding a write grant on it
    /// does not. Both drives are created with anonymous reads off, as in the original.
    /// </summary>
    public static IEnumerable<object[]> ArchivalCases()
    {
        yield return [CallerSpec.Owner(Drive1()), true];
        yield return [CallerSpec.App(Drive1(), DrivePermission.Write), false];
        yield return [CallerSpec.Guest(Drive1(), DrivePermission.Write), false];
    }

    private static DriveSpec Drive1() =>
        new(TargetDrive.NewTargetDrive(), "drive 1", AllowAnonymousReads: false);

    [Test, Explicit]
    [TestCaseSource(nameof(ArchivalCases))]
    public async Task CanArchiveDriveAndDriveListIsCorrectlyReturned(CallerSpec spec, bool shouldHaveArchivedDrive)
    {
        // Prepare
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var drive1 = spec.TargetDrive;
        var driveType = drive1.Type;
        var drive2 = TargetDrive.NewTargetDrive(driveType);

        await owner.Admin.CreateDrive(drive2, "drive 2", allowAnonymousReads: false);

        var drivesByTypeResponse = await caller.V1.Drive.GetDrivesByType(driveType);
        Assert.That(drivesByTypeResponse.IsSuccessStatusCode, Is.True);
        var drivesByType = drivesByTypeResponse.Content;
        Assert.That(drivesByType.Results.Select(p => p.TargetDrive), Does.Contain(drive1));
        Assert.That(drivesByType.Results.Select(p => p.TargetDrive), Does.Contain(drive2));

        // Act - set archive on drive 1
        var setFlagResponse = await owner.Admin.SetArchiveFlag(drive1, true);
        Assert.That(setFlagResponse.IsSuccessStatusCode, Is.True);

        //
        // Assert
        //
        var updatedDrivesByTypeResponse = await caller.V1.Drive.GetDrivesByType(driveType);
        Assert.That(updatedDrivesByTypeResponse.IsSuccessStatusCode, Is.True);
        var updatedDrivesList = updatedDrivesByTypeResponse.Content;
        var updatedTargetDrives = updatedDrivesList.Results.Select(p => p.TargetDrive).ToList();

        if (shouldHaveArchivedDrive)
        {
            Assert.That(updatedTargetDrives, Does.Contain(drive1));
        }
        else
        {
            Assert.That(updatedTargetDrives, Does.Not.Contain(drive1));
        }

        Assert.That(updatedTargetDrives, Does.Contain(drive2));
    }

    [Test, Explicit]
    [TestCaseSource(nameof(ArchivalCases))]
    public async Task CanUnarchiveDriveAndDriveReturnedFromResults(CallerSpec spec, bool shouldHaveArchivedDrive)
    {
        // Prepare
        var (caller, owner) = await SetupCallerWithOwner(spec);

        var drive1 = spec.TargetDrive;
        var driveType = drive1.Type;
        var drive2 = TargetDrive.NewTargetDrive(driveType);

        await owner.Admin.CreateDrive(drive2, "drive 2", allowAnonymousReads: false);

        var drivesByTypeResponse = await caller.V1.Drive.GetDrivesByType(driveType);
        var drivesByType = drivesByTypeResponse.Content;
        Assert.That(drivesByType.Results.Select(p => p.TargetDrive), Does.Contain(drive1));
        Assert.That(drivesByType.Results.Select(p => p.TargetDrive), Does.Contain(drive2));

        // Act - set archive on drive 1
        var setFlagResponse = await owner.Admin.SetArchiveFlag(drive1, true);
        Assert.That(setFlagResponse.IsSuccessStatusCode, Is.True);

        // now set unarhive and ensure we can see it

        var unarchiveDriveResponse = await owner.Admin.SetArchiveFlag(drive1, false);
        Assert.That(unarchiveDriveResponse.IsSuccessStatusCode, Is.True);

        //
        // Assert
        //
        var updatedDrivesByTypeResponse = await caller.V1.Drive.GetDrivesByType(driveType);
        Assert.That(updatedDrivesByTypeResponse.IsSuccessStatusCode, Is.True);
        var updatedDrivesList = updatedDrivesByTypeResponse.Content;
        var updatedTargetDrives = updatedDrivesList.Results.Select(p => p.TargetDrive).ToList();

        Assert.That(updatedTargetDrives, Does.Contain(drive1));
        Assert.That(updatedTargetDrives, Does.Contain(drive2));
    }

    [Test]
    public async Task FailToArchiveSystemDrive()
    {
        // Prepare
        var owner = await LoginAsOwner();
        var driveManagement = owner.RefitFor<IRefitDriveManagement>();

        // Act - archive each protected system drive
        foreach (var drive in BuiltinDrives.Protected)
        {
            var setFlagResponse = await driveManagement.SetArchiveDriveFlag(new UpdateDriveArchiveFlag
            {
                TargetDrive = drive,
                Archived = true,
            });

            Assert.That(setFlagResponse.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), $"drive {drive}");
        }
    }
}
