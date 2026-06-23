using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Core;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version9tov10;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Verifies the v9 → v10 migration: the system connection circles
/// (<see cref="SystemCircleConstants.ConfirmedConnectionsCircleId"/> and
/// <see cref="SystemCircleConstants.AutoConnectionsCircleId"/>) grant
/// <see cref="DrivePermission.Read"/> on the <see cref="SystemDriveConstants.ProfileDrive"/>, and — the
/// real goal — every connected member's circle grant ends up carrying the drive's <b>storage key</b>
/// (<see cref="RedactedDriveGrant.HasStorageKey"/>) so they can decrypt ProfileDrive data.
///
/// A freshly provisioned identity already ships with the grant (anonymous-read drives are auto-granted
/// to the system circles when created), so each test first <b>downgrades</b> the stored circle
/// definitions to their pre-v10 shape — removing the ProfileDrive grant — before establishing the
/// connection, so the member's snapshot lacks it. The migration then backfills it. The migration is
/// driven directly out of the tenant scope under a real owner (master-key) context, the way
/// <c>VersionUpgradeService</c> drives it in production.
/// </summary>
[TestFixture]
public class V9ToV10ProfileDriveMigrationTests : V2Fixture
{
    protected override string[] HostIdentities => [Identities.Frodo, Identities.Sam];

    [Test]
    public async Task V10_GrantsProfileDriveStorageKey_ToConnectedMember()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);
        var sam = await LoginAsOwner(Identities.Sam);

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, frodo);
        var circleDefinitionService = scope.Resolve<CircleDefinitionService>();

        // Reproduce the pre-v10 state: strip the ProfileDrive grant from the system circle definitions
        // BEFORE connecting, so the member's circle-grant snapshot is issued without it.
        await RemoveProfileDriveGrantAsync(circleDefinitionService, SystemCircleConstants.ConfirmedConnectionsCircleId);
        await RemoveProfileDriveGrantAsync(circleDefinitionService, SystemCircleConstants.AutoConnectionsCircleId);

        // Connect: Frodo (IdentityOwner origin) → Sam lands in Frodo's ConfirmedConnections circle.
        await frodo.Connections.SendConnectionRequest(sam.Identity);
        await sam.Connections.AcceptConnectionRequest(frodo.Identity);

        // Precondition: Sam's confirmed-connections grant has no ProfileDrive grant (hence no storage key).
        var before = await GetProfileDriveGrantAsync(frodo, sam, SystemCircleConstants.ConfirmedConnectionsCircleId);
        Assert.That(before, Is.Null,
            "precondition: the connected member should not yet have a ProfileDrive grant");

        var migration = scope.Resolve<V9ToV10VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        // The member's ProfileDrive grant now exists and carries the storage key — i.e. they can decrypt.
        var after = await GetProfileDriveGrantAsync(frodo, sam, SystemCircleConstants.ConfirmedConnectionsCircleId);
        Assert.That(after, Is.Not.Null, "migration should grant the connected member ProfileDrive Read");
        Assert.That(after!.PermissionedDrive.Permission.HasFlag(DrivePermission.Read), Is.True);
        Assert.That(after.HasStorageKey, Is.True,
            "the ProfileDrive grant must carry the storage key so the member can decrypt the data");
    }

    [Test]
    public async Task V10_GrantsProfileDriveRead_ToSystemCircleDefinitions()
    {
        var frodo = await LoginAsOwner(Identities.Frodo);

        var scope = Host.GetTenantScope(frodo.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, frodo);
        var circleDefinitionService = scope.Resolve<CircleDefinitionService>();

        // Downgrade both system circles to their pre-v10 shape (no ProfileDrive grant).
        await RemoveProfileDriveGrantAsync(circleDefinitionService, SystemCircleConstants.ConfirmedConnectionsCircleId);
        await RemoveProfileDriveGrantAsync(circleDefinitionService, SystemCircleConstants.AutoConnectionsCircleId);

        var confirmedBefore = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId);
        var autoBefore = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.AutoConnectionsCircleId);
        Assert.That(HasProfileDriveRead(confirmedBefore), Is.False,
            "precondition: confirmed-connections circle should not yet grant ProfileDrive Read");
        Assert.That(HasProfileDriveRead(autoBefore), Is.False,
            "precondition: auto-connections circle should not yet grant ProfileDrive Read");

        var migration = scope.Resolve<V9ToV10VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        // Second run must be a no-op (grant already present) and still validate.
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var confirmedAfter = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsCircleId);
        var autoAfter = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.AutoConnectionsCircleId);
        Assert.That(HasProfileDriveRead(confirmedAfter), Is.True,
            "migration should grant ProfileDrive Read to the confirmed-connections circle");
        Assert.That(HasProfileDriveRead(autoAfter), Is.True,
            "migration should grant ProfileDrive Read to the auto-connections circle");

        // The grant must not be duplicated by a repeated run.
        Assert.That(confirmedAfter.DriveGrants.Count(g => g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive), Is.EqualTo(1),
            "ProfileDrive grant should appear exactly once after repeated migration runs");

        // Existing drive grants are preserved (both circles ship with a ChatDrive grant).
        Assert.That(confirmedAfter.DriveGrants.Any(g => g.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive), Is.True,
            "the existing ChatDrive grant on the confirmed-connections circle must be preserved");
    }

    private async Task<RedactedDriveGrant> GetProfileDriveGrantAsync(OwnerSession owner, OwnerSession member, GuidId circleId)
    {
        var info = await owner.Connections.GetConnectionInfo(member.Identity);
        Assert.That(info.IsSuccessStatusCode, Is.True, "GetConnectionInfo should succeed");
        Assert.That(info.Content!.Status, Is.EqualTo(ConnectionStatus.Connected), "the member should be connected");

        var circleGrant = info.Content.AccessGrant.CircleGrants.SingleOrDefault(cg => cg.CircleId == circleId);
        return circleGrant?.DriveGrants?.SingleOrDefault(g =>
            g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive);
    }

    private static async Task RemoveProfileDriveGrantAsync(CircleDefinitionService service, GuidId circleId)
    {
        var def = await service.GetCircleAsync(circleId);
        def.DriveGrants = def.DriveGrants
            .Where(g => g.PermissionedDrive.Drive != SystemDriveConstants.ProfileDrive)
            .ToList();
        await service.UpdateAsync(def, skipValidation: true);
    }

    private static bool HasProfileDriveRead(CircleDefinition circle)
    {
        return circle?.DriveGrants?.Any(g =>
            g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive &&
            g.PermissionedDrive.Permission.HasFlag(DrivePermission.Read)) ?? false;
    }

    /// <summary>
    /// Builds an owner context carrying the master key by replaying the production path used by
    /// <c>VersionUpgradeService</c> (<see cref="OwnerAuthenticationService.UpdateOdinContextAsync"/>).
    /// </summary>
    private async Task<IOdinContext> BuildOwnerContextAsync(ILifetimeScope scope, OwnerSession owner)
    {
        var authService = scope.Resolve<OwnerAuthenticationService>();
        var odinContext = new OdinContext
        {
            Tenant = default,
            AuthTokenCreated = null,
            Caller = null
        };
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            AccessRegistrationId = null,
            DevicePushNotificationKey = null,
            ClientIdOrDomain = null
        };

        await authService.UpdateOdinContextAsync(owner.Token, clientContext, odinContext);
        odinContext.Caller.AssertHasMasterKey();
        return odinContext;
    }
}
