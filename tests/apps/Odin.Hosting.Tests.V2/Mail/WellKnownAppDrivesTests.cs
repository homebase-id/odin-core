using System.Linq;
using NUnit.Framework;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Mail;

/// <summary>
/// EmailAppDrive used to be defined by NOT being a system drive -- the owner approved it through
/// extend-permissions and the server never created it. That stopped being true when Email became a
/// built-in app: its registration is granted ReadWrite on the drive, and a grant cannot be issued for a
/// drive that does not exist, so the drive is now seeded.
///
/// What is worth guarding is what replaced it: seeded and immutable are the same set. Anything
/// <c>EnsureSystemDrivesExist</c> creates must be in <see cref="SystemDriveConstants.SystemDrives"/>, or
/// the owner can archive a drive the system depends on -- in this case one holding the OpenPGP secret
/// keyrings.
/// </summary>
public class WellKnownAppDrivesTests
{
    [Test]
    public void EmailAppDriveIsProtectedFromOwnerModification()
    {
        Assert.That(
            SystemDriveConstants.SystemDrives.Contains(WellKnownAppDrives.EmailAppDrive),
            Is.True,
            "EmailAppDrive is seeded, so it must also be immutable — DriveManager guards on this list");
    }

    [Test]
    public void EmailAppDriveAliasCollidesWithNoOtherSystemDrive()
    {
        // The alias IS the drive id (DriveManager.CreateDriveAsync), so a collision would not be a
        // naming clash — it would be the same storage.
        Assert.That(
            SystemDriveConstants.SystemDrives.Count(d => d.Alias == WellKnownAppDrives.EmailAppDrive.Alias),
            Is.EqualTo(1));
    }

    [Test]
    public void EmailAppDriveIsFullyPopulated()
    {
        Assert.That(WellKnownAppDrives.EmailAppDrive.Alias.Value, Is.Not.EqualTo(System.Guid.Empty));
        Assert.That(WellKnownAppDrives.EmailAppDrive.Type.Value, Is.Not.EqualTo(System.Guid.Empty));
        Assert.That(WellKnownAppDrives.EmailAppDrive.Alias, Is.Not.EqualTo(WellKnownAppDrives.EmailAppDrive.Type));
    }
}
