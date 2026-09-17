#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.Database.Identity.Table;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version18tov19;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Covers the v18 -&gt; v19 pass: the one that leaves no drive and no circle without an owning app.
/// </summary>
/// <remarks>
/// Rows written before the rule carry a null <c>AppId</c>, which is what made a drive slug
/// unconstrained -- NULLs do not collide in a unique index, so <c>UNIQUE(identityId, AppId, DriveSlug)</c>
/// covers nothing for them.  The pass stamps the owner console, which is what "the owner's own" already
/// meant, and fills any address the row is missing.
/// <para>
/// Nothing creates an ownerless row any more, so these tests make one the only way left: writing the
/// column back to null underneath the service that would refuse to.
/// </para>
/// </remarks>
[TestFixture]
public class OwnershipMigrationTests : V2Fixture
{
    [Test]
    public async Task AnOwnerlessDriveIsGivenToTheOwnerConsoleWithAnAddress()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Field Notes", allowAnonymousReads: false);

        var (scope, ctx) = await MigrationContextAsync(owner);
        var driveId = (await scope.Resolve<DriveManager>().GetDriveAsync(drive.Alias))!.Id;

        await StripDriveOwnershipAsync(scope, driveId);

        var stamped = await scope.Resolve<V18ToV19VersionMigrationService>()
            .StampOwnerConsoleDrivesAsync(ctx, CancellationToken.None);

        Assert.That(stamped, Is.GreaterThanOrEqualTo(1));

        var after = (await scope.Resolve<DriveManager>().GetDriveAsync(drive.Alias))!;
        Assert.That(after.AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId));
        Assert.That(after.DriveSlug, Is.Not.Null.And.Not.Empty, "an owned drive has to be addressable");

        // A drive of a type nothing recognises still gets a readable category rather than a null half.
        Assert.That(after.DriveTypeSlug, Is.EqualTo(DriveSlugGenerator.DefaultTypeSlug));
    }

    [Test]
    public async Task ADriveAlreadyOwnedByAnAppIsLeftAlone()
    {
        var owner = await LoginAsOwner(Identities.Sam);

        var appId = await owner.Admin.RegisterBareApp();

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "App Drive", allowAnonymousReads: false, appId: appId,
            driveSlug: "appdrive");

        var (scope, ctx) = await MigrationContextAsync(owner);

        await scope.Resolve<V18ToV19VersionMigrationService>()
            .StampOwnerConsoleDrivesAsync(ctx, CancellationToken.None);

        var after = (await scope.Resolve<DriveManager>().GetDriveAsync(drive.Alias))!;
        Assert.That(after.AppId, Is.EqualTo(appId), "the pass fills owners, it does not move them");
        Assert.That(after.DriveSlug, Is.EqualTo("appdrive"), "and it does not re-derive an address");
    }

    [Test]
    public async Task AnOwnerlessCircleIsGivenToTheOwnerConsole()
    {
        var owner = await LoginAsOwner(Identities.Merry);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Book Club", ReadCircleMembershipGrant());

        var (scope, ctx) = await MigrationContextAsync(owner);

        await StripCircleOwnershipAsync(scope, circleId);
        Assert.That((await scope.Resolve<CircleDefinitionService>().GetCircleAsync(circleId))!.AppId, Is.Null,
            "precondition: the circle has to start the way a pre-v19 row does");

        var stamped = await scope.Resolve<V18ToV19VersionMigrationService>()
            .StampOwnerConsoleCirclesAsync(ctx, CancellationToken.None);

        Assert.That(stamped, Is.GreaterThanOrEqualTo(1));

        var after = await scope.Resolve<CircleDefinitionService>().GetCircleAsync(circleId);
        Assert.That(after!.AppId, Is.EqualTo(SystemAppConstants.OwnerConsoleAppId));
    }

    [Test]
    public async Task TheSystemCirclesAreStampedToo()
    {
        // They belong to no app today, which is the last place nulls survive. An app is still refused
        // them afterwards -- what changes is that the refusal asks whose they are rather than whether
        // anyone's.
        var owner = await LoginAsOwner(Identities.Pippin);

        // One circle of the owner's own, so the sweep has something of each kind to find alongside the
        // two the platform provisions.
        await owner.Admin.CreateCircle(Guid.NewGuid(), "Walking Club", ReadCircleMembershipGrant());

        var (scope, ctx) = await MigrationContextAsync(owner);
        var circles = scope.Resolve<CircleDefinitionService>();

        foreach (var circle in await circles.GetCirclesAsync(includeSystemCircle: true))
        {
            await StripCircleOwnershipAsync(scope, circle.Id);
        }

        await scope.Resolve<V18ToV19VersionMigrationService>()
            .StampOwnerConsoleCirclesAsync(ctx, CancellationToken.None);

        var afterAll = await circles.GetCirclesAsync(includeSystemCircle: true);
        Assert.That(afterAll.Any(), Is.True, "precondition: the identity has circles to stamp");
        Assert.That(afterAll.All(c => c.AppId != null), Is.True, "no circle may be left ownerless");
    }

    [Test]
    public async Task ValidationPassesOnceEverythingIsStamped()
    {
        var owner = await LoginAsOwner(Identities.TomBombadil);

        var drive = TargetDrive.NewTargetDrive();
        await owner.Admin.CreateDrive(drive, "Ad Hoc", allowAnonymousReads: false);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Ad Hoc Circle", ReadCircleMembershipGrant());

        var (scope, ctx) = await MigrationContextAsync(owner);
        var driveId = (await scope.Resolve<DriveManager>().GetDriveAsync(drive.Alias))!.Id;

        await StripDriveOwnershipAsync(scope, driveId);
        await StripCircleOwnershipAsync(scope, circleId);

        var v19 = scope.Resolve<V18ToV19VersionMigrationService>();
        await v19.UpgradeAsync(ctx, CancellationToken.None);

        // Fails the upgrade rather than logging: everything above it now takes "there is always an
        // owner" as a given.
        Assert.DoesNotThrowAsync(async () => await v19.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    private static PermissionSetGrantRequest ReadCircleMembershipGrant() => new()
    {
        PermissionSet = new PermissionSet(PermissionKeys.ReadCircleMembership)
    };

    /// <summary>
    /// Writes a drive's owner and address back to null, the state a row written before v19 is in.
    /// </summary>
    /// <remarks>
    /// Straight at the table: <c>DriveManager</c> has no path that produces this any more, which is the
    /// point of the migration.
    /// </remarks>
    private static async Task StripDriveOwnershipAsync(ILifetimeScope scope, Guid driveId)
    {
        // Through the cached table, not the raw one: it invalidates on write under the tag
        // DriveManager's own read cache is keyed with, so the next read is not the pre-strip drive.
        var drives = scope.Resolve<TableDrivesCached>();
        var record = await drives.GetAsync(driveId);

        record!.AppId = null;
        record.DriveSlug = null;
        record.DriveTypeSlug = null;

        await drives.UpsertAsync(record);
    }

    private static async Task StripCircleOwnershipAsync(ILifetimeScope scope, Guid circleId)
    {
        var db = scope.Resolve<IdentityDatabase>();
        var record = await db.CircleCached.GetAsync(circleId);

        record!.AppId = null;

        await db.CircleCached.UpsertAsync(record);
    }

    private async Task<(ILifetimeScope scope, IOdinContext ctx)> MigrationContextAsync(OwnerSession owner)
    {
        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        return (scope, await BuildOwnerContextAsync(scope, owner));
    }

    private static async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext { Tenant = default, AuthTokenCreated = null, Caller = null };
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        await authService.UpdateOdinContextAsync(owner.Token, clientContext, odinContext);
        odinContext.Caller!.AssertHasMasterKey();
        return odinContext;
    }
}
