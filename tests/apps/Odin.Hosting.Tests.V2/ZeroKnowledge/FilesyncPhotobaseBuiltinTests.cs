#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Membership.Circles;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.ZeroKnowledge;

/// <summary>
/// Filesync and Photobase are built-in (#2136): every identity has their apps and circles, and their SelectiveShare drives
/// once the owner's browser makes them at a password login.  An identity that installed either first keeps what it has.
/// The old photo-app's definitions are retired, so an identity that used it can uninstall it.
/// </summary>
[TestFixture]
public class FilesyncPhotobaseBuiltinTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly (Guid AppId, TargetDrive Drive, CircleDefinition Circle)[] Apps =
    [
        (SystemAppConstants.FilesyncAppId, WellKnownAppDrives.FilesyncDrive, BuiltinCircles.FilesyncCircle),
        (SystemAppConstants.PhotobaseAppId, WellKnownAppDrives.PhotobaseDrive, BuiltinCircles.PhotobaseCircle)
    ];

    private const DrivePermission ReadReactComment = DrivePermission.Read | DrivePermission.React | DrivePermission.Comment;

    [Test]
    public async Task ANewIdentityGetsBothAppsAndCircles_AndTheirDrivesSelectiveShareAtAPasswordLogin()
    {
        var (frodo, zkMasterKey) = await ZeroKnowledgeFrodoAsync();
        await AssertWaitingForTheirDrivesAsync(frodo);

        await ZkOwnerSteps.CreateMissingDrivesAsync(frodo, zkMasterKey);

        await AssertProvisionedAsync(frodo);
    }

    /// <summary>
    /// The upgrade's pre-pass creates no zero-knowledge drive and the provisioner waits for one, so an existing identity
    /// gets both from <c>missing-drives</c>, as a new one does.
    /// </summary>
    [Test]
    public async Task AnExistingIdentityWithoutThemGetsThemThroughTheUpgradeThenMissingDrives()
    {
        var (frodo, zkMasterKey) = await ZeroKnowledgeFrodoAsync();
        var (scope, ctx) = await MigrationContextAsync(frodo);
        await scope.Resolve<BuiltinProvisioner>().EnsureAllAsync(ctx);
        await AssertWaitingForTheirDrivesAsync(frodo);

        await ZkOwnerSteps.CreateMissingDrivesAsync(frodo, zkMasterKey);

        await AssertProvisionedAsync(frodo);
    }

    [Test]
    public async Task AnIdentityThatInstalledThemFirstKeepsItsDrivesCirclesMembersAndGrants()
    {
        var (frodo, zkMasterKey) = await ZeroKnowledgeFrodoAsync();
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);
        var (scope, ctx) = await MigrationContextAsync(frodo);

        // Installed as the apps install themselves before this: the drive made with the browser's keys, then the app
        // registered declaring its circle, which gets an id of its own.  Sam is a member.
        var installed = new Dictionary<Guid, (StorageDrive Drive, CircleDefinition Circle, RedactedAppRegistration App)>();
        foreach (var (appId, drive, circle) in Apps)
        {
            var definition = BuiltinApps.Builtin.Single(a => a.AppId == appId).Drives.Single();
            var keys = ZkBrowser.MakeDriveKeys(drive.Alias, zkMasterKey);
            await scope.Resolve<IDriveManager>().CreateDriveAsync(new CreateDriveRequest
            {
                Name = definition.Name, TargetDrive = drive, Metadata = "", AppId = appId, DriveSlug = definition.DriveSlug,
                DriveTypeSlug = definition.DriveTypeSlug, SharingModel = DriveSharingModel.SelectiveShare, ZkKeys = keys.Request
            }, ctx);
            await scope.Resolve<IAppRegistrationService>().RegisterAppAsync(new AppRegistrationRequest
            {
                AppId = appId,
                Name = "installed first",
                AppSlug = BuiltinApps.Builtin.Single(a => a.AppId == appId).AppSlug,
                PermissionSet = new PermissionSet(),
                Drives = [Grant(drive, DrivePermission.ReadWrite | DrivePermission.React | DrivePermission.Comment)],
                Circles =
                [
                    new CreateAppCircleRequest
                    {
                        Name = circle.Name, Slug = circle.Slug, Description = circle.Description, Emoji = circle.Emoji,
                        DriveGrants = [Grant(drive, ReadReactComment)], Permissions = new PermissionSet()
                    }
                ]
            }, ctx);

            var appCircle = await scope.Resolve<CircleDefinitionService>().GetCircleByAppSlugAsync(appId, circle.Slug);
            Assert.That(appCircle!.Id.Value, Is.Not.EqualTo(circle.Id.Value), "precondition: the install made its own circle");
            await scope.Resolve<CircleNetworkService>().EnrollInCircleAsync(appCircle.Id, sam.Identity, ctx);

            var app = (await frodo.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest { AppId = appId })).Content!;
            installed[appId] = ((await scope.Resolve<IDriveManager>().GetDriveAsync(drive.Alias))!, appCircle, app);
        }

        // Then the upgrade, and the browser's next password login
        await scope.Resolve<BuiltinProvisioner>().EnsureAllAsync(ctx);
        var missing = (await ZkOwnerSteps.Client(frodo).GetMissingDrives()).Content!;
        Assert.That(missing.Select(d => d.TargetDrive), Has.None.AnyOf(Apps.Select(a => a.Drive).ToArray()));
        foreach (var (_, drive, _) in Apps)
        {
            await scope.Resolve<BuiltinProvisioner>().EnsureAppOfBrowserMadeDriveAsync(drive.Alias, ctx);
        }

        var circles = (await frodo.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions(includeSystemCircle: true)).Content!;
        var samsCircles = (await frodo.Connections.GetConnectionInfo(sam.Identity)).Content!.AccessGrant.CircleGrants
            .Select(c => c.CircleId).ToList();
        foreach (var (appId, drive, circle) in Apps)
        {
            var (driveBefore, circleBefore, appBefore) = installed[appId];
            var driveAfter = (await scope.Resolve<IDriveManager>().GetDriveAsync(drive.Alias))!;
            Assert.That(driveAfter.Data.ZkMasterKeyEncryptedStorageKey, Is.EqualTo(driveBefore.Data.ZkMasterKeyEncryptedStorageKey),
                "the same drive key");
            Assert.That(driveAfter.Created, Is.EqualTo(driveBefore.Created));

            var appCircles = circles.Where(c => c.AppId == appId).ToList();
            Assert.That(appCircles.Select(c => c.Id.Value), Is.EqualTo(new[] { circleBefore.Id.Value }),
                "the installed circle, not a second one");
            Assert.That(appCircles.Single().IsTreeDeclared, Is.True, "it is the app's declared circle");
            Assert.That(samsCircles, Does.Contain(circleBefore.Id), "Sam is still a member");
            Assert.That(circles.Any(c => c.Id == circle.Id), Is.False);

            var appAfter = (await frodo.RefitFor<IRefitOwnerAppRegistration>().GetRegisteredApp(new GetAppRequest { AppId = appId })).Content!;
            Assert.That(appAfter.Name, Is.EqualTo(appBefore.Name), "the installed registration, not replaced");
            Assert.That(appAfter.Grant.DriveGrants.Select(g => (g.PermissionedDrive.Drive, g.PermissionedDrive.Permission)),
                Is.EquivalentTo(appBefore.Grant.DriveGrants.Select(g => (g.PermissionedDrive.Drive, g.PermissionedDrive.Permission))));
        }
    }

    /// <summary>
    /// The old photo-app (<c>32f0bdbf…</c>) was on the tree, so it couldn't be uninstalled and its circle couldn't be
    /// deleted.  Its definitions are gone: it uninstalls like any app, and Photobase, which shares its drive slug, is
    /// untouched.
    /// </summary>
    [Test]
    public async Task TheOldPhotoAppUninstallsWithItsDriveAndCircle_AndPhotobaseIsUntouched()
    {
        var (frodo, zkMasterKey) = await ZeroKnowledgeFrodoAsync();
        await ZkOwnerSteps.CreateMissingDrivesAsync(frodo, zkMasterKey);

        var oldAppId = Guid.Parse("32f0bdbf-017f-4fc0-8004-2d4631182d1e");
        var oldDrive = new TargetDrive
        {
            Alias = Guid.Parse("6483b7b1f71bd43eb6896c86148668cc"),
            Type = Guid.Parse("2af68fe72fb84896f39f97c59d60813a")
        };
        var oldCircleId = Guid.Parse("d9471317728a431c94d9842118acb676");
        await frodo.Admin.EnsureAppRegistered(oldAppId);
        await frodo.Admin.CreateDrive(oldDrive, "Photo Library", allowAnonymousReads: false, appId: oldAppId,
            driveSlug: new OdinSlug("photos"), driveTypeSlug: new OdinSlug("photos"));
        await frodo.Admin.CreateCircle(oldCircleId, "Photos", new PermissionSetGrantRequest
        {
            Drives = [Grant(oldDrive, DrivePermission.Read)],
            PermissionSet = new PermissionSet()
        }, oldAppId);

        var registrations = frodo.RefitFor<IRefitOwnerAppRegistration>();
        Assert.That((await registrations.GetRegisteredApp(new GetAppRequest { AppId = oldAppId })).Content!.IsBuiltIn, Is.False);
        var uninstall = await registrations.Uninstall(new UninstallAppRequest { AppId = oldAppId, DeleteOwnedCirclesAndDrives = true });
        Assert.That(uninstall.StatusCode, Is.EqualTo(HttpStatusCode.OK), uninstall.Error?.Content);

        var drives = await frodo.Admin.GetDrives();
        Assert.That(drives.Any(d => d.TargetDriveInfo == oldDrive), Is.False, "the old drive went with its app");
        Assert.That(drives.Any(d => d.TargetDriveInfo == WellKnownAppDrives.PhotobaseDrive), Is.True, "Photobase's drive stays");
        var circles = (await frodo.RefitFor<IRefitOwnerCircleDefinition>().GetCircleDefinitions(includeSystemCircle: true)).Content!;
        Assert.That(circles.Any(c => c.Id == oldCircleId), Is.False, "the old circle went with its app");
        Assert.That(circles.Any(c => c.Id == BuiltinCircles.PhotobaseCircle.Id), Is.True, "Photobase's circle stays");
        Assert.That((await registrations.GetRegisteredApp(new GetAppRequest { AppId = SystemAppConstants.PhotobaseAppId })).Content,
            Is.Not.Null, "Photobase stays");
    }

    private static async Task AssertWaitingForTheirDrivesAsync(OwnerSession owner)
    {
        var missing = (await ZkOwnerSteps.Client(owner).GetMissingDrives()).Content!;
        var registrations = owner.RefitFor<IRefitOwnerAppRegistration>();
        foreach (var (appId, drive, _) in Apps)
        {
            var request = missing.SingleOrDefault(d => d.TargetDrive == drive);
            Assert.That(request?.SharingModel, Is.EqualTo(DriveSharingModel.SelectiveShare), $"{drive.Alias} is missing, SelectiveShare");
            Assert.That(request!.AppId, Is.EqualTo(appId));
            var app = await registrations.GetRegisteredApp(new GetAppRequest { AppId = appId });
            Assert.That(app.IsSuccessStatusCode && app.Content != null, Is.False, "the app waits for its drive");
        }
    }

    private static async Task AssertProvisionedAsync(OwnerSession owner)
    {
        var registrations = owner.RefitFor<IRefitOwnerAppRegistration>();
        foreach (var (appId, drive, circle) in Apps)
        {
            var created = await owner.Admin.GetDrive(drive);
            Assert.That(created.SharingModel, Is.EqualTo(DriveSharingModel.SelectiveShare));
            Assert.That(created.AppId, Is.EqualTo(appId));

            var app = (await registrations.GetRegisteredApp(new GetAppRequest { AppId = appId })).Content;
            Assert.That(app?.Grant.DriveGrants.Select(g => g.PermissionedDrive.Drive), Does.Contain(drive), "registered with its drive");
            Assert.That(app!.IsBuiltIn, Is.True);

            var definition = await owner.Admin.GetCircleDefinition(circle.Id);
            Assert.That(definition.AppId, Is.EqualTo(appId));
            var grant = definition.DriveGrants.Single().PermissionedDrive;
            Assert.That((grant.Drive, grant.Permission), Is.EqualTo((drive, ReadReactComment)));
        }

        Assert.That((await ZkOwnerSteps.Client(owner).GetMissingDrives()).Content, Is.Empty);
    }

    private static DriveGrantRequest Grant(TargetDrive drive, DrivePermission permission) =>
        new() { PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = permission } };
}
