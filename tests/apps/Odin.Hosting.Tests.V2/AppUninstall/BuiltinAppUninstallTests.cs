#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Odin.Core.Cryptography.Crypto;
using Odin.Hosting.Controllers.OwnerToken.AppManagement;
using Odin.Hosting.Controllers.OwnerToken.Drive;
using Odin.Hosting.Tests._Universal.ApiClient.Owner.DriveManagement;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Hosting.Tests.OwnerApi.ApiClient.Apps;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Hosting.Tests._V2.ApiClient;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.JobManagement;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.AppUninstall;

/// <summary>
/// Uninstalling an optional known app, even one the platform provisions (#2138): its drives, circles and registration
/// go, no other app or circle keeps a grant on its drives, an upgrade doesn't bring it back, and registering it again
/// does.  The core apps are still refused, and so is deleting a system drive or a built-in circle any other way.
/// </summary>
[TestFixture]
public class BuiltinAppUninstallTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly Guid[] UninstallableProvisionedApps =
    [
        SystemAppConstants.EmailAppId,
        SystemAppConstants.LocationAppId,
        SystemAppConstants.MomentsAppId,
        SystemAppConstants.WebdropAppId,
        SystemAppConstants.VaultAppId,
        SystemAppConstants.ListsAppId,
        SystemAppConstants.MailAppId,
    ];

    private static readonly Guid[] CoreApps =
    [
        SystemAppConstants.ChatAppId,
        SystemAppConstants.ContactsAppId,
        SystemAppConstants.FeedAppId,
        SystemAppConstants.HomePageAppId,
        SystemAppConstants.RecoveryAppId,
        SystemAppConstants.SystemAppId,
    ];

    /// <summary>Every drive a known app owns, the carry-overs without a tree entry (Lists, Mail) included.</summary>
    private static readonly CreateDriveRequest[] KnownDrives =
        [..BuiltinApps.AllDrives, BuiltinDrives.ListsDrive, BuiltinDrives.MailDrive];

    private IJobManager Jobs => Host.Server.Services.GetRequiredService<IJobManager>();

    /// <summary>
    /// A new app added to the tree has to be put on one side or the other here, so whether the owner may remove it
    /// is decided, not defaulted.
    /// </summary>
    [Test]
    public void EveryBuiltinAppIsEitherCoreOrUninstallable()
    {
        Assert.That(BuiltinApps.Builtin.Where(a => !a.Uninstallable).Select(a => a.AppId), Is.EquivalentTo(CoreApps));
        Assert.That(BuiltinApps.Builtin.Where(a => a.Uninstallable).Select(a => a.AppId),
            Is.EquivalentTo(UninstallableProvisionedApps.Except([SystemAppConstants.ListsAppId, SystemAppConstants.MailAppId])));
        Assert.That(BuiltinApps.Wellknown.All(a => a.Uninstallable), Is.True, "every known, unprovisioned app is optional");

        Assert.That(UninstallableProvisionedApps.All(BuiltinApps.IsUninstallable), Is.True);
        Assert.That(UninstallableProvisionedApps.All(BuiltinProvisioner.IsProvisioned), Is.True);
        Assert.That(CoreApps.Any(BuiltinApps.IsUninstallable), Is.False);
    }

    [TestCaseSource(nameof(CoreApps))]
    public async Task ACoreAppIsStillRefused(Guid appId)
    {
        var owner = await LoginAsOwner();
        Assert.That(await UninstallAsync(owner, appId), Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(await owner.Admin.IsAppRegistered(appId), Is.True);
    }

    [TestCaseSource(nameof(UninstallableProvisionedApps))]
    public async Task AnUninstalledProvisionedAppIsGoneAndStaysGoneAcrossTheUpgrades(Guid appId)
    {
        var owner = await LoginAsOwner();
        await InstallAsTheOwnerHasItAsync(owner, appId);
        var (scope, ctx) = await MigrationContextAsync(owner);
        Assert.That(await scope.Resolve<IDriveManager>().GetDrivesByAppIdAsync(appId, ctx), Is.Not.Empty,
            "arrange: the app owns no drive");

        Assert.That(await UninstallAsync(owner, appId, deleteOwned: true), Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await owner.Admin.IsAppRegistered(appId), Is.False);
        await AssertGoneAsync(owner, appId, "after the uninstall");

        await RunTheWholeLadderAsync(owner);
        await scope.Resolve<BuiltinProvisioner>().EnsureAllAsync(ctx);

        Assert.That(await owner.Admin.IsAppRegistered(appId), Is.False, "the upgrade registered it again");
        await AssertGoneAsync(owner, appId, "after the upgrade");
        Assert.That((await scope.Resolve<BuiltinProvisioner>().GetMissingZeroKnowledgeDrivesAsync()).Any(d => d.AppId == appId),
            Is.False, "the owner's browser is asked to create its drive");
    }

    [TestCaseSource(nameof(UninstallableProvisionedApps))]
    public async Task RegisteringAnUninstalledAppAgainBringsItBackAsOnANewIdentity(Guid appId)
    {
        var owner = await LoginAsOwner();
        await InstallAsTheOwnerHasItAsync(owner, appId);
        Assert.That(await UninstallAsync(owner, appId, deleteOwned: true), Is.EqualTo(HttpStatusCode.OK));
        await RunPurgeAsync(owner);

        // Granted its own server-made drives, as its install asks: they have to be back before the grant is made.
        var serverMade = KnownDrives.Where(d => d.AppId == appId && !d.SharingModel.IsZeroKnowledgeDrive()).ToList();
        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            Drives = serverMade.Select(d => new DriveGrantRequest
            {
                PermissionedDrive = new PermissionedDrive { Drive = d.TargetDrive, Permission = DrivePermission.ReadWrite }
            }).ToList()
        });

        var (scope, ctx) = await MigrationContextAsync(owner);
        var driveManager = scope.Resolve<IDriveManager>();
        foreach (var drive in serverMade)
        {
            Assert.That(await driveManager.GetDriveAsync(drive.TargetDrive.Alias), Is.Not.Null, $"{drive.Name} is not back");
        }

        var browserMade = BuiltinApps.BrowserMadeDrives.Where(d => d.AppId == appId).Select(d => d.TargetDrive).ToList();
        var missing = (await scope.Resolve<BuiltinProvisioner>().GetMissingZeroKnowledgeDrivesAsync()).Select(d => d.TargetDrive);
        Assert.That(missing, Is.SupersetOf(browserMade), "the owner's browser isn't asked to create its drive again");

        // Its circles come with its drives, or once the browser has made them.
        var circles = (await scope.Resolve<CircleDefinitionService>().GetCirclesAsync()).Select(c => c.Id.Value).ToList();
        var expected = BuiltinApps.SeededCircles.Where(c => c.AppId == appId).Select(c => c.Id.Value).ToList();
        if (browserMade.Count == 0)
        {
            Assert.That(circles, Is.SupersetOf(expected), "its circles are not back");
        }
        else
        {
            Assert.That(circles, Has.None.Matches<Guid>(expected.Contains), "its circles came before its drive");
        }

        await scope.Resolve<BuiltinProvisioner>().EnsureAllAsync(ctx);
        Assert.That(await owner.Admin.IsAppRegistered(appId), Is.True);
    }

    [TestCase("community")]
    [TestCase("social-sync")]
    public async Task AKnownAppThatIsNotProvisionedIsUninstalledWithItsCircles(string slug)
    {
        var app = BuiltinApps.Wellknown.Single(a => a.AppSlug.ToString() == slug);
        var owner = await LoginAsOwner();
        await owner.Admin.RegisterApp(app.AppId, new PermissionSetGrantRequest());
        var (scope, _) = await MigrationContextAsync(owner);
        var definitions = scope.Resolve<CircleDefinitionService>();
        foreach (var circle in app.Circles)
        {
            await definitions.EnsureCircleExistsAsync(circle);
        }

        Assert.That(await UninstallAsync(owner, app.AppId, deleteOwned: true), Is.EqualTo(HttpStatusCode.OK));

        Assert.That(await owner.Admin.IsAppRegistered(app.AppId), Is.False);
        foreach (var circle in app.Circles)
        {
            Assert.That(await definitions.GetCircleAsync(circle.Id), Is.Null, $"{circle.Name} outlived its app");
        }
    }

    [Test]
    public async Task ASystemDriveOrBuiltInCircleIsStillRefusedOutsideAnUninstall()
    {
        var owner = await LoginAsOwner();
        var deleteDrive = await owner.RefitFor<IRefitDriveManagement>().DeleteDrive(
            new TargetDriveRequest { TargetDrive = WellKnownAppDrives.MomentsDrive });
        Assert.That(deleteDrive.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));

        var (scope, _) = await MigrationContextAsync(owner);
        var definitions = scope.Resolve<CircleDefinitionService>();
        Assert.ThrowsAsync<Odin.Core.Exceptions.OdinClientException>(() =>
            definitions.AssertDeletableAsync(BuiltinCircles.MomentsCircle.Id));

        // Nor does naming an app that doesn't own the circle, or a core app, let one through.
        Assert.ThrowsAsync<Odin.Core.Exceptions.OdinClientException>(() =>
            definitions.AssertDeletableAsync(BuiltinCircles.MomentsCircle.Id, SystemAppConstants.LocationAppId));
        Assert.ThrowsAsync<Odin.Core.Exceptions.OdinClientException>(() =>
            definitions.AssertDeletableAsync(BuiltinCircles.ChatCircle.Id, SystemAppConstants.ChatAppId));

        Assert.That(await scope.Resolve<IDriveManager>().GetDriveAsync(WellKnownAppDrives.MomentsDrive.Alias), Is.Not.Null);
        Assert.That(await definitions.GetCircleAsync(BuiltinCircles.MomentsCircle.Id), Is.Not.Null);
    }

    [Test]
    public async Task AConnectionLosesOnlyTheUninstalledAppsCircle()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.ConnectAsync(frodo, sam);

        var review = await new V2ConnectionNetworkClient(frodo.Identity, frodo.Factory).MarkReviewedAsync(sam.Identity);
        Assert.That(review.IsSuccessStatusCode, Is.True, "arrange: review failed");
        var enrollment = (await frodo.Admin.GrantCircleToMany(BuiltinCircles.MomentsCircle.Id, [sam.Identity])).Content!;
        Assert.That(enrollment.Enrolled, Is.EqualTo(1), "arrange: Sam was not enrolled in Moments");

        var before = (await frodo.Connections.GetConnectionInfo(sam.Identity)).Content!.AccessGrant;
        var otherCircles = before.CircleGrants.Select(c => c.CircleId.Value)
            .Where(id => id != BuiltinCircles.MomentsCircle.Id.Value).ToList();
        Assert.That(otherCircles, Does.Contain(BuiltinCircles.ChatCircle.Id.Value), "arrange: Sam is not in Chat");

        Assert.That(await UninstallAsync(frodo, SystemAppConstants.MomentsAppId, deleteOwned: true), Is.EqualTo(HttpStatusCode.OK));

        var after = (await frodo.Connections.GetConnectionInfo(sam.Identity)).Content!.AccessGrant;
        Assert.That(after.CircleGrants.Select(c => c.CircleId.Value), Is.EquivalentTo(otherCircles),
            "Sam kept the Moments circle, or lost another");
        Assert.That(after.CircleGrants.SelectMany(c => c.DriveGrants).Any(g => g.PermissionedDrive.Drive == WellKnownAppDrives.MomentsDrive),
            Is.False, "Sam still holds a grant on the Moments drive");
    }

    /// <summary>
    /// Registers what a provisioned app has on an identity whose owner uses it: Lists and Mail are registered by
    /// their own install, and Email's and Vault's drives are made by the owner's browser.
    /// </summary>
    private async Task InstallAsTheOwnerHasItAsync(OwnerSession owner, Guid appId)
    {
        if (BuiltinApps.BrowserMadeDrives.Any(d => d.AppId == appId))
        {
            await GiveTheDrivesAnOlderIdentityHasAsync(owner);
        }

        await owner.Admin.EnsureAppRegistered(appId);
    }

    private async Task AssertGoneAsync(OwnerSession owner, Guid appId, string when)
    {
        var (scope, ctx) = await MigrationContextAsync(owner);
        var driveManager = scope.Resolve<IDriveManager>();
        var drives = KnownDrives.Where(d => d.AppId == appId).Select(d => d.TargetDrive).ToList();
        foreach (var drive in drives)
        {
            Assert.That(await driveManager.GetDriveAsync(drive.Alias), Is.Null, $"its drive is there {when}");
        }

        var circles = await scope.Resolve<CircleDefinitionService>().GetCirclesAsync();
        Assert.That(circles.Where(c => c.AppId == appId), Is.Empty, $"its circle is there {when}");
        Assert.That(circles.SelectMany(c => c.DriveGrants ?? []).Any(g => drives.Contains(g.PermissionedDrive.Drive)), Is.False,
            $"a circle grants its drive {when}");

        foreach (var app in await scope.Resolve<IAppRegistrationService>().GetRegisteredAppsAsync(ctx))
        {
            var granted = (app.Grant?.DriveGrants ?? []).Select(g => g.PermissionedDrive.Drive)
                .Concat((app.CircleMemberPermissionSetGrantRequest?.Drives ?? []).Select(g => g.PermissionedDrive.Drive));
            Assert.That(granted.Any(drives.Contains), Is.False, $"{app.Name} grants its drive {when}");
        }
    }

    /// <summary>Winds the identity back to v0 and runs the version ladder to the release version.</summary>
    private async Task RunTheWholeLadderAsync(OwnerSession owner)
    {
        var (scope, _) = await MigrationContextAsync(owner);
        var config = scope.Resolve<TenantConfigService>();
        await config.ForceVersionNumberAsync(0);

        var (iv, encryptedToken) = AesCbc.Encrypt(owner.Token.ToPortableBytes(), scope.Resolve<TenantContext>().TemporalEncryptionKey);
        await scope.Resolve<VersionUpgradeService>().UpgradeAsync(new VersionUpgradeJobData
        {
            Tenant = owner.Identity,
            Iv = iv,
            EncryptedToken = encryptedToken
        }, CancellationToken.None);

        // UpgradeAsync records a failure rather than throwing it; the log names the phase.
        var failure = await config.GetVersionFailureInfoAsync();
        Assert.That(failure, Is.Null, $"the ladder failed at v{failure?.FailedDataVersionNumber}; see the log");
        Assert.That((await config.GetVersionInfoAsync()).DataVersionNumber, Is.EqualTo(Odin.Services.Version.DataVersionNumber));
    }

    /// <summary>Runs the owner's drive purge jobs until none is left: the test host does not run jobs on its own.</summary>
    private async Task RunPurgeAsync(OwnerSession owner)
    {
        var identityId = Host.GetTenantScope(owner.Identity.DomainName).Resolve<TenantContext>().DotYouRegistryId;
        for (var runs = 0; runs < 20; runs++)
        {
            var jobs = (await Jobs.GetJobsByIdentityIdAsync(identityId))
                .Where(j => j.jobType == DrivePurgeJob.JobTypeId.ToString())
                .ToList();
            if (jobs.Count == 0)
            {
                return;
            }

            foreach (var job in jobs)
            {
                await Jobs.RunJobNowAsync(job.id, CancellationToken.None);
            }
        }

        Assert.Fail("the purge didn't finish");
    }

    private static async Task<HttpStatusCode> UninstallAsync(OwnerSession owner, Guid appId, bool deleteOwned = false) =>
        (await owner.RefitFor<IRefitOwnerAppRegistration>().Uninstall(new UninstallAppRequest
        {
            AppId = appId,
            DeleteOwnedCirclesAndDrives = deleteOwned
        })).StatusCode;
}
