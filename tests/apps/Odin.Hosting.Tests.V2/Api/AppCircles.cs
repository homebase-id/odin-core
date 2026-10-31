using System;
using System.Threading.Tasks;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Api;

/// <summary>Arrange helpers for circles an app owns.</summary>
public static class AppCircles
{
    /// <summary>An app with no permission keys, its own drive, and a circle of its own granting Write on it.</summary>
    public static async Task<(AppSession App, Guid CircleId, TargetDrive Drive)> AppWithItsOwnCircleAsync(OwnerSession owner)
    {
        var appId = Guid.NewGuid();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "the app's", allowAnonymousReads: false);
        var app = await AppSession.SetupAsync(owner, drive, DrivePermission.All, [], knownAppId: appId);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "the app's circle", new PermissionSetGrantRequest { Drives = [WriteOn(drive)] }, appId);
        return (app, circleId, drive);
    }

    public static DriveGrantRequest WriteOn(TargetDrive drive) =>
        new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Write } };
}
