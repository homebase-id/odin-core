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
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version18tov19;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Covers the v18 -&gt; v19 pass: the one that leaves no circle without an owning app.
/// </summary>
/// <remarks>
/// Rows written before the rule carry a null <c>AppId</c>.  The pass stamps the owner console, which is
/// what "the owner's own" already meant.  Nothing creates an ownerless circle any more, so these tests
/// make one the only way left: writing the column back to null underneath the service that would refuse
/// to.  Drives are not covered here: their addresses are filled by the Drives NOT NULL migration, tested
/// in <c>DrivesNotNullMigrationTests</c>.
/// </remarks>
[TestFixture]
public class OwnershipMigrationTests : V2Fixture
{
    [Test]
    public async Task AnOwnerlessCircleIsGivenToTheOwnerConsole()
    {
        var owner = await LoginAsOwner(Identities.Merry);

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Book Club", OwnerAdmin.ReadCircleMembershipGrant());

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
        await owner.Admin.CreateCircle(Guid.NewGuid(), "Walking Club", OwnerAdmin.ReadCircleMembershipGrant());

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

        var circleId = Guid.NewGuid();
        await owner.Admin.CreateCircle(circleId, "Ad Hoc Circle", OwnerAdmin.ReadCircleMembershipGrant());

        var (scope, ctx) = await MigrationContextAsync(owner);

        await StripCircleOwnershipAsync(scope, circleId);

        var v19 = scope.Resolve<V18ToV19VersionMigrationService>();
        await v19.UpgradeAsync(ctx, CancellationToken.None);

        // Fails the upgrade rather than logging: everything above it now takes "there is always an
        // owner" as a given.
        Assert.DoesNotThrowAsync(async () => await v19.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    private static async Task StripCircleOwnershipAsync(ILifetimeScope scope, Guid circleId)
    {
        var db = scope.Resolve<IdentityDatabase>();
        var record = await db.CircleCached.GetAsync(circleId);

        record!.AppId = null;

        await db.CircleCached.UpsertAsync(record);
    }

}
