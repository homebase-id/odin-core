#nullable enable
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Configuration.VersionUpgrade.Version19tov20;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Covers the v19 -&gt; v20 pass: Family, Friends and Work grant Read on the ProfileDrive, Feed grants Read
/// on Public Posts, and every existing member is re-minted so the grant carries the storage key.
/// </summary>
/// <remarks>
/// A fresh identity already has these grants from <see cref="BuiltinCircles"/>, so these tests take one
/// away first to stand in for an identity provisioned before the change.
/// </remarks>
[TestFixture]
public class BuiltinCircleReadGrantMigrationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task AnExistingMember_IsGivenTheProfileDriveKey()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var network = scope.Resolve<CircleNetworkService>();
        var family = BuiltinCircles.FamilyCircle.Id;

        // Family had no drives before v20, so rewind the definition first and add the member after:
        // their grant is then minted from the old definition, as it would have been.
        await RewindDefinitionAsync(scope, family, WellKnownAppDrives.ProfileDrive);
        await network.EnrollInCircleAsync(family, sam.Identity, ctx);
        Assert.That(await ProfileDriveGrantAsync(scope, sam), Is.Null,
            "precondition: the member's Family grant should predate the ProfileDrive Read");

        var migration = scope.Resolve<V19ToV20VersionMigrationService>();
        await migration.GrantReadToBuiltinCirclesAsync(ctx, CancellationToken.None);

        var def = await scope.Resolve<CircleDefinitionService>().GetCircleAsync(family);
        Assert.That(def!.DriveGrants.Any(g => g.PermissionedDrive.Drive == WellKnownAppDrives.ProfileDrive), Is.True);

        var grant = await ProfileDriveGrantAsync(scope, sam);
        Assert.That(grant, Is.Not.Null, "the member's grant should have been re-minted with the new drive");
        Assert.That(grant!.KeyStoreKeyEncryptedStorageKey, Is.Not.Null,
            "Read without the storage key cannot decrypt anything; the key is the point");

        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    [Test]
    public async Task TheOwnersOwnEditsSurvive_AndRunningTwiceAddsNothing()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var (scope, ctx) = await MigrationContextAsync(frodo);
        var feed = BuiltinCircles.FeedCircle.Id;

        await RewindDefinitionAsync(scope, feed, WellKnownAppDrives.PublicPostsChannelDrive);

        // An owner edit the built-in definition does not have: the pass adds, it does not reset.
        var edited = await scope.Resolve<CircleDefinitionService>().GetCircleAsync(feed);
        edited!.DriveGrants = edited.DriveGrants.Append(ReadOn(WellKnownAppDrives.HomePageConfigDrive)).ToList();
        await scope.Resolve<CircleNetworkService>().UpdateCircleDefinitionAsync(edited, ctx);

        var migration = scope.Resolve<V19ToV20VersionMigrationService>();
        await migration.GrantReadToBuiltinCirclesAsync(ctx, CancellationToken.None);
        await migration.GrantReadToBuiltinCirclesAsync(ctx, CancellationToken.None);

        var def = await scope.Resolve<CircleDefinitionService>().GetCircleAsync(feed);
        var drives = def!.DriveGrants.Select(g => g.PermissionedDrive.Drive).ToList();

        Assert.That(drives.Count(d => d == WellKnownAppDrives.PublicPostsChannelDrive), Is.EqualTo(1));
        Assert.That(drives, Does.Contain(WellKnownAppDrives.HomePageConfigDrive), "the owner's edit must survive");
        Assert.That(drives, Does.Contain(WellKnownAppDrives.FeedDrive));
    }

    [Test]
    public async Task ADepositThePrePassSkipped_IsQueuedForItsAppToRedo()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var storage = scope.Resolve<CircleNetworkStorage>();
        var family = BuiltinCircles.FamilyCircle;
        var depositingApp = System.Guid.NewGuid();

        // Stands in for a deposit the pre-pass could not convert: sealed before v20, so without the
        // ProfileDrive key.
        var icr = await storage.GetAsync(sam.Identity);
        icr!.PeerKeyStore.DepositedGrants.Add(new DepositedGrant
        {
            CircleId = family.Id,
            DepositingAppId = depositingApp,
            Deposited = UnixTimeUtc.Now()
        });
        await storage.UpsertAsync(icr, ctx);

        var migration = scope.Resolve<V19ToV20VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while a deposit would convert without the key");

        var requeued = await migration.RequeueStaleDepositsAsync(ctx, CancellationToken.None);
        Assert.That(requeued.Select(r => r.DomainName), Does.Contain(sam.Identity.DomainName));

        var after = (await storage.GetAsync(sam.Identity))!.PeerKeyStore;
        Assert.That(after.DepositedGrants.Any(d => d.CircleId == family.Id), Is.False);

        var pending = after.PendingEnrollments.SingleOrDefault(p => p.CircleId == family.Id);
        Assert.That(pending, Is.Not.Null, "the owning app should be left to redo it");
        Assert.That(pending!.OwningAppId, Is.EqualTo(family.AppId));
        Assert.That(pending.RequestedByAppId, Is.EqualTo(depositingApp), "who asked for it is kept");

        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    private static DriveGrantRequest ReadOn(TargetDrive drive) => new()
    {
        PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read }
    };

    /// <summary>
    /// Rewinds a circle's definition to before v20.  Validation is skipped the way built-in provisioning
    /// skips it: Family had no grants at all then, which the checked path refuses.
    /// </summary>
    private static async Task RewindDefinitionAsync(ILifetimeScope scope, System.Guid circleId, TargetDrive drive)
    {
        var definitions = scope.Resolve<CircleDefinitionService>();
        var def = await definitions.GetCircleAsync(circleId);
        def!.DriveGrants = def.DriveGrants.Where(g => g.PermissionedDrive.Drive != drive).ToList();
        await definitions.UpdateAsync(def, skipValidation: true);
    }

    private static async Task<DriveGrant?> ProfileDriveGrantAsync(ILifetimeScope scope, OwnerSession member)
    {
        var icr = await scope.Resolve<CircleNetworkStorage>().GetAsync(member.Identity);
        Assert.That(icr, Is.Not.Null);
        Assert.That(icr!.PeerKeyStore.CircleGrants.TryGetValue(BuiltinCircles.FamilyCircle.Id, out var circleGrant), Is.True,
            "precondition: the member should be in Family");
        return circleGrant!.KeyStoreKeyEncryptedDriveGrants
            .FirstOrDefault(g => g.PermissionedDrive.Drive == WellKnownAppDrives.ProfileDrive);
    }
}
