#nullable enable
using System;
using System.Linq;
using NUnit.Framework;
using Odin.Core;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Tests.Base;

/// <summary>
/// What a group with no permission keys shows in a redacted security context: its drive grants to an app (Filesync
/// registers with none and reads its drive's key from there), and nothing to any other caller, as before.
/// </summary>
public class PermissionGroupRedactionTest
{
    private static readonly TargetDrive Drive = TargetDrive.NewTargetDrive();

    private static PermissionGroup GroupWithNoPermissionKeys() => new(null!,
    [
        new DriveGrant
        {
            DriveId = Drive.Alias,
            PermissionedDrive = new PermissionedDrive { Drive = Drive, Permission = DrivePermission.Read }
        }
    ], null, null);

    [Test]
    public void AnAppSeesTheDriveGrantsOfAGroupWithNoPermissionKeys()
    {
        var app = new OdinClientContext { AppId = Guid.NewGuid() };
        var redacted = GroupWithNoPermissionKeys().Redacted(app);
        Assert.That(redacted.DriveGrants.Select(g => g.PermissionedDrive.Drive), Is.EqualTo(new[] { Drive }));
    }

    [Test]
    public void AnyOtherCallerSeesNoneAsBefore()
    {
        var guest = new OdinClientContext { ClientIdOrDomain = "sam.dotyou.cloud" };
        Assert.That(GroupWithNoPermissionKeys().Redacted(guest).DriveGrants.Select(g => g.PermissionedDrive.Drive), Is.Empty);
        Assert.That(GroupWithNoPermissionKeys().Redacted().DriveGrants.Select(g => g.PermissionedDrive.Drive), Is.Empty);
    }
}
