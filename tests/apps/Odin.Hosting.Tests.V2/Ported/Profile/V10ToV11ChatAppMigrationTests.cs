using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autofac;
using NUnit.Framework;
using Odin.Hosting.Tests.V2.Api;
using Odin.Services.Apps;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version10tov11;
using Odin.Services.Drives;

namespace Odin.Hosting.Tests.V2.Ported.Profile;

/// <summary>
/// Verifies the v10 → v11 migration: the Chat app (<see cref="SystemAppConstants.ChatAppId"/>) is
/// granted <see cref="DrivePermission.Write"/> on the <see cref="SystemDriveConstants.ProfileDrive"/> and
/// the <see cref="PermissionKeys.ManageProfile"/> permission key, so writes can funnel through
/// <c>ProfileAttributeService</c>. Runs the migration service directly out of the tenant scope under a
/// real owner (master-key) context, mirroring how <c>VersionUpgradeService</c> drives it in production.
/// Each test re-registers the Chat app (already auto-provisioned by <see cref="V2Fixture"/>) with its
/// pre-v11 shape (Read-only ProfileDrive, no ManageProfile) before running the migration.
/// </summary>
[TestFixture]
public class V10ToV11ChatAppMigrationTests : V2Fixture
{
    [Test]
    public async Task V11_GrantsProfileDriveWriteAndManageProfile_ToChatApp_PreservingExistingGrants()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        // A pre-v11 Chat app: Read-only on the ProfileDrive, no ManageProfile, plus an unrelated drive
        // grant and permission key that must survive the migration untouched.
        await owner.Admin.RegisterApp(SystemAppConstants.ChatAppId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ChatDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                },
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var before = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(HasProfileDriveWrite(before!), Is.False,
            "precondition: chat app should not yet have ProfileDrive Write");
        Assert.That(HasManageProfile(before!), Is.False,
            "precondition: chat app should not yet have ManageProfile");

        var migration = scope.Resolve<V10ToV11VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(HasProfileDriveWrite(after!), Is.True,
            "migration should grant ProfileDrive Write to the chat app");
        Assert.That(HasManageProfile(after!), Is.True,
            "migration should grant ManageProfile to the chat app");

        var profileGrant = after!.Grant.DriveGrants.Single(g => g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive);
        Assert.That(profileGrant.PermissionedDrive.Permission, Is.EqualTo(DrivePermission.ReadWrite),
            "the ProfileDrive grant should end up exactly ReadWrite, not merely carrying the Write flag");

        // Pre-existing drive grant and permission key are preserved.
        Assert.That(after.Grant.DriveGrants.Any(g => g.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive), Is.True,
            "the existing ChatDrive grant must be preserved");
        Assert.That(after.Grant.PermissionSet.HasKey(PermissionKeys.ReadConnections), Is.True,
            "the existing ReadConnections key must be preserved");
    }

    [Test]
    public async Task V11_SkipsRevokedChatApp_AndPreservesRevocation()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        await owner.Admin.RegisterApp(SystemAppConstants.ChatAppId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        await apps.RevokeAppAsync(SystemAppConstants.ChatAppId, ctx);

        var migration = scope.Resolve<V10ToV11VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);

        // A revoked chat app has nothing to validate.
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        // UpdateAppPermissionsAsync rebuilds the exchange grant, which would reset IsRevoked — the
        // migration must leave a revoked chat app untouched.
        var after = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(after!.IsRevoked, Is.True, "the migration must not un-revoke a revoked chat app");
        Assert.That(HasProfileDriveWrite(after), Is.False,
            "a revoked chat app must not be granted ProfileDrive Write");
        Assert.That(HasManageProfile(after), Is.False,
            "a revoked chat app must not be granted ManageProfile");
    }

    [Test]
    public async Task V11_IsIdempotent()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        await owner.Admin.RegisterApp(SystemAppConstants.ChatAppId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.Read
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();
        var migration = scope.Resolve<V10ToV11VersionMigrationService>();

        await migration.UpgradeAsync(ctx, CancellationToken.None);
        // Second run must be a no-op (grant already present) and still validate.
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(HasProfileDriveWrite(after!), Is.True);
        Assert.That(HasManageProfile(after!), Is.True);

        // The grant must not be duplicated by a repeated run.
        Assert.That(after!.Grant.DriveGrants.Count(g => g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive), Is.EqualTo(1),
            "ProfileDrive grant should appear exactly once after repeated migration runs");
    }

    private static bool HasProfileDriveWrite(RedactedAppRegistration app)
    {
        return app.Grant?.DriveGrants?.Any(g =>
            g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive &&
            g.PermissionedDrive.Permission.HasFlag(DrivePermission.Write)) ?? false;
    }

    private static bool HasManageProfile(RedactedAppRegistration app)
    {
        return app.Grant?.PermissionSet?.HasKey(PermissionKeys.ManageProfile) ?? false;
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
