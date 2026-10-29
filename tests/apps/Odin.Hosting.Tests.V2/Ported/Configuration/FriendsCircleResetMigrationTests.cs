#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Tests.V2.Api;
using Odin.Hosting.Tests.V2.Peer;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Configuration.VersionUpgrade.Version20tov21;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Configuration;

/// <summary>
/// Covers the v20 -&gt; v21 pass (#1947): Friends is reset to its built-in drive grants, and the retired Work,
/// Acquaintances and Family circles are deleted where they have no members.
/// </summary>
/// <remarks>
/// A fresh identity has neither problem, so each test first puts the identity in the state an older one is in:
/// a Friends circle the owner extended with another drive, or the retired circles still defined.
/// </remarks>
[TestFixture]
public class FriendsCircleResetMigrationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    private static readonly TargetDrive ExtraDrive = WellKnownAppDrives.HomePageConfigDrive;

    [Test]
    public async Task AnExtendedFriendsCircle_IsResetAndItsMembersLoseTheExtraDrive()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var friends = BuiltinCircles.FriendsCircle.Id;

        // The owner added a drive Contacts holds no key for, then put Sam in: his grant carries it.
        var definitions = scope.Resolve<CircleDefinitionService>();
        var def = await definitions.GetCircleAsync(friends);
        def!.DriveGrants = def.DriveGrants.Append(ReadOn(ExtraDrive)).ToList();
        await definitions.UpdateAsync(def, skipValidation: true);
        await scope.Resolve<CircleNetworkService>().EnrollInCircleAsync(friends, sam.Identity, ctx);
        Assert.That(await MemberDrivesAsync(scope, sam), Does.Contain(ExtraDrive),
            "precondition: the member's Friends grant should carry the extra drive");

        var migration = scope.Resolve<V20ToV21VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while Friends grants more than the built-in definition");

        await migration.ResetFriendsDriveGrantsAsync(ctx, CancellationToken.None);
        await migration.ResetFriendsDriveGrantsAsync(ctx, CancellationToken.None);

        var after = await definitions.GetCircleAsync(friends);
        Assert.That(after!.DriveGrants.Select(g => g.PermissionedDrive.Drive),
            Is.EquivalentTo(BuiltinCircles.FriendsCircle.DriveGrants.Select(g => g.PermissionedDrive.Drive)));

        var memberDrives = await MemberDrivesAsync(scope, sam);
        Assert.That(memberDrives, Does.Not.Contain(ExtraDrive), "the member's grant should be re-minted without it");
        Assert.That(memberDrives, Does.Contain(WellKnownAppDrives.ProfileDrive));

        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    [Test]
    public async Task EmptyRetiredCircles_AreDeleted_AndOneWithAMemberIsKept()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);
        await PeerFlow.CreatePeerDriveAsync(frodo, sam, DrivePermission.Read, "baseline");

        var (scope, ctx) = await MigrationContextAsync(frodo);
        var definitions = scope.Resolve<CircleDefinitionService>();

        var (work, acquaintances, family) = (RetiredCircle(0, "Work"), RetiredCircle(1, "Acquaintances"), RetiredCircle(2, "Family"));
        foreach (var circle in new[] { work, acquaintances, family })
        {
            await definitions.EnsureCircleExistsAsync(circle);
        }

        await scope.Resolve<CircleNetworkService>().EnrollInCircleAsync(family.Id, sam.Identity, ctx);

        var migration = scope.Resolve<V20ToV21VersionMigrationService>();
        Assert.ThrowsAsync<OdinSystemException>(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None),
            "validation must not pass while an empty retired circle exists");

        await migration.DeleteEmptyRetiredCirclesAsync(ctx, CancellationToken.None);
        await migration.DeleteEmptyRetiredCirclesAsync(ctx, CancellationToken.None);

        Assert.That(await definitions.GetCircleAsync(work.Id), Is.Null);
        Assert.That(await definitions.GetCircleAsync(acquaintances.Id), Is.Null);
        Assert.That(await definitions.GetCircleAsync(family.Id), Is.Not.Null, "a circle with a member is the owner's");

        Assert.DoesNotThrowAsync(() => migration.ValidateUpgradeAsync(ctx, CancellationToken.None));
    }

    private static CircleDefinition RetiredCircle(int index, string name) => new()
    {
        Id = V20ToV21VersionMigrationService.RetiredCircleIds[index],
        Name = name,
        Slug = new OdinSlug(name),
        Description = name,
        AppId = SystemAppConstants.ContactsAppId,
        GrantOn = CircleGrantOn.None,
        Designation = CircleDesignation.Personal,
        DriveGrants = [ReadOn(WellKnownAppDrives.ProfileDrive)],
        Permissions = new PermissionSet { Keys = [] }
    };

    private static DriveGrantRequest ReadOn(TargetDrive drive) => new()
    {
        PermissionedDrive = new PermissionedDrive { Drive = drive, Permission = DrivePermission.Read }
    };

    private static async Task<TargetDrive[]> MemberDrivesAsync(ILifetimeScope scope, OwnerSession member)
    {
        var icr = await scope.Resolve<CircleNetworkStorage>().GetAsync(member.Identity);
        Assert.That(icr, Is.Not.Null);
        Assert.That(icr!.PeerKeyStore.CircleGrants.TryGetValue(BuiltinCircles.FriendsCircle.Id, out var circleGrant), Is.True,
            "precondition: the member should be in Friends");
        return circleGrant!.KeyStoreKeyEncryptedDriveGrants.Select(g => g.PermissionedDrive.Drive).ToArray();
    }
}
