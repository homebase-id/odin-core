using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.DriveDeletion;

/// <summary>
/// Deleting a whole drive (#1869): archived and non-system only, every file and directory gone, and no grant
/// left naming it -- so a drive re-created with the same alias, as an app reinstall does, inherits nothing.
/// </summary>
[TestFixture]
public class DriveDeleteTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AnActiveDriveIsRefused()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "active", allowAnonymousReads: false);

        var response = await DeleteAsync(owner, drive);

        Assert.That(response, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await owner.Admin.GetDrives()).Any(d => d.TargetDriveInfo == drive), Is.True);
    }

    [Test]
    public async Task ASystemDriveIsRefused()
    {
        var owner = await LoginAsOwner();

        Assert.That(await DeleteAsync(owner, BuiltinDrives.Protected[0]), Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task AnArchivedDriveIsDeletedAndItsAliasCanBeReused()
    {
        var owner = await LoginAsOwner();
        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "doomed", allowAnonymousReads: false);
        var file = await DriveDeletionTests.UploadAsync(owner, drive, "content");
        await owner.Admin.SetArchiveFlag(drive, archived: true);

        Assert.That(await DeleteAsync(owner, drive), Is.EqualTo(HttpStatusCode.OK));

        Assert.That((await owner.Admin.GetDrives()).Any(d => d.TargetDriveInfo == drive), Is.False);
        Assert.That(Directory.Exists(DrivePath(owner, drive)), Is.False, "drive directory left behind");

        // the alias is free again, and the new drive starts empty
        await owner.Admin.CreateDrive(drive, "reborn", allowAnonymousReads: false);
        Assert.That((await owner.V1.Drive.GetFileHeader(file)).StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task NoGrantNamesTheDriveAfterwards()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        // Sam is connected through a circle of Frodo's that grants write on the drive
        var drive = await PeerFlow.CreatePeerDriveAsync(sam, frodo, DrivePermission.Write, allowAnonymousReads: false);
        Assert.That(SamsDriveGrants(await frodo.Connections.GetConnectionInfo(sam.Identity), drive), Is.Not.Empty,
            "arrange: Sam holds no grant on the drive");

        // and an app holds it, for itself and for its circles' members
        var appId = Guid.NewGuid();
        var onDrive = new PermissionSetGrantRequest
        {
            Drives = [new DriveGrantRequest { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read } }]
        };
        await frodo.Admin.RegisterApp(appId, onDrive, circleMemberGrantRequest: onDrive);

        await frodo.Admin.SetArchiveFlag(drive, archived: true);
        Assert.That(await DeleteAsync(frodo, drive), Is.EqualTo(HttpStatusCode.OK));

        Assert.That(SamsDriveGrants(await frodo.Connections.GetConnectionInfo(sam.Identity), drive), Is.Empty,
            "Sam's circle grant still names the drive");

        var circles = (await frodo.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions(includeSystemCircle: true)).Content!;
        Assert.That(circles.Where(c => c.DriveGrants.Any(g => g.PermissionedDrive.Drive == drive)).Select(c => c.Name), Is.Empty,
            "a circle definition still grants the drive");

        var app = (await frodo.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest { AppId = appId })).Content!;
        Assert.That(app.Grant.DriveGrants.Any(g => g.PermissionedDrive.Drive == drive), Is.False, "the app's grant still names the drive");
        Assert.That(app.CircleMemberPermissionSetGrantRequest.Drives.Any(g => g.PermissionedDrive.Drive == drive), Is.False,
            "the app's circle-member grant still names the drive");
    }

    private static async Task<HttpStatusCode> DeleteAsync(OwnerSession owner, TargetDrive drive) =>
        (await owner.RefitFor<IRefitOwnerDriveDeletion>().DeleteDrive(new TargetDriveRequest { TargetDrive = drive })).StatusCode;

    private static System.Collections.Generic.IEnumerable<object> SamsDriveGrants(
        Refit.ApiResponse<Odin.Services.Membership.Connections.RedactedIdentityConnectionRegistration> info, TargetDrive drive) =>
        info.Content!.AccessGrant.CircleGrants.SelectMany(c => c.DriveGrants).Where(g => g.PermissionedDrive.Drive == drive);

    private string DrivePath(OwnerSession owner, TargetDrive drive) =>
        Host.GetTenantScope(owner.Identity.DomainName).Resolve<TenantContext>().TenantPathManager.GetDrivePath(drive.Alias);
}
