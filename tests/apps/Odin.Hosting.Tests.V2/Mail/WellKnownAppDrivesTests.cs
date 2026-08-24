using System.Linq;
using NUnit.Framework;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// Guards the one property that makes <see cref="WellKnownAppDrives"/> a separate file from
/// <see cref="SystemDriveConstants"/>: these drives are well-known but NOT system drives. A
/// system drive is auto-created for every tenant, which would make "the drive exists" useless as
/// a signal and would hand every app a drive nobody asked for.
/// </summary>
public class WellKnownAppDrivesTests
{
    [Test]
    public void EmailAppDriveIsNotASystemDrive()
    {
        Assert.That(
            SystemDriveConstants.SystemDrives.Contains(WellKnownAppDrives.EmailAppDrive),
            Is.False,
            "EmailAppDrive must not be auto-created — the owner approves it through extend-permissions");
    }

    [Test]
    public void EmailAppDriveAliasCollidesWithNoSystemDrive()
    {
        // The alias IS the drive id (DriveManager.CreateDriveAsync), so a collision would not be a
        // naming clash — it would be the same storage.
        Assert.That(
            SystemDriveConstants.SystemDrives.Any(d => d.Alias == WellKnownAppDrives.EmailAppDrive.Alias),
            Is.False);
    }

    [Test]
    public void EmailAppDriveIsFullyPopulated()
    {
        Assert.That(WellKnownAppDrives.EmailAppDrive.Alias.Value, Is.Not.EqualTo(System.Guid.Empty));
        Assert.That(WellKnownAppDrives.EmailAppDrive.Type.Value, Is.Not.EqualTo(System.Guid.Empty));
        Assert.That(WellKnownAppDrives.EmailAppDrive.Alias, Is.Not.EqualTo(WellKnownAppDrives.EmailAppDrive.Type));
    }
}
