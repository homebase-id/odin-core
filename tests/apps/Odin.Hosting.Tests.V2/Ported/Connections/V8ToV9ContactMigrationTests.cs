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
using Odin.Services.Configuration.VersionUpgrade.Version8tov9;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;

namespace Odin.Hosting.Tests.V2.Ported.Connections;

/// <summary>
/// Verifies the v8 → v9 migration: existing apps that hold <b>write</b> access to the ContactDrive are
/// granted the new <see cref="PermissionKeys.ManageContacts"/> permission (required by the Contact
/// API), while apps without contact-drive write access are left untouched. Runs the migration service
/// directly out of the tenant scope under a real owner (master-key) context, mirroring how
/// <c>VersionUpgradeService</c> drives it in production.
/// </summary>
[TestFixture]
public class V8ToV9ContactMigrationTests : V2Fixture
{
    [Test]
    public async Task V9_GrantsManageContacts_ToAppWithContactDriveWriteAccess()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();

        // An app that writes the contact drive but predates the ManageContacts key.
        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var before = await apps.GetAppRegistration(appId, ctx);
        Assert.That(before!.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts), Is.False,
            "precondition: app should not yet have ManageContacts");

        var migration = scope.Resolve<V8ToV9VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(appId, ctx);
        Assert.That(after!.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts), Is.True,
            "migration should grant ManageContacts to an app with contact-drive write access");
        // Existing keys are preserved.
        Assert.That(after.Grant.PermissionSet.HasKey(PermissionKeys.ReadConnections), Is.True);
    }

    [Test]
    public async Task V9_LeavesAppWithoutContactDriveWriteAccess_Untouched()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();

        // Read-only on the contact drive → not a writer → must not get ManageContacts.
        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.Read
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var migration = scope.Resolve<V8ToV9VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(appId, ctx);
        Assert.That(after!.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts), Is.False,
            "an app with only Read on the contact drive must not be granted ManageContacts");
    }

    [Test]
    public async Task V9_SkipsRevokedApp_AndPreservesRevocation()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();

        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        await apps.RevokeAppAsync(appId, ctx);

        var migration = scope.Resolve<V8ToV9VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        // Updating permissions rebuilds the exchange grant, which would reset IsRevoked — the
        // migration must leave revoked apps untouched.
        var after = await apps.GetAppRegistration(appId, ctx);
        Assert.That(after!.IsRevoked, Is.True, "the migration must not un-revoke a revoked app");
        Assert.That(after.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts), Is.False,
            "a revoked app must not be granted ManageContacts");
    }

    [Test]
    public async Task V9_GrantsManageContacts_ToAppWithNullPermissionSet()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();

        // A drives-only registration: PermissionSet is never validated, so a stored null is legal
        // and must not crash the migration.
        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = null
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var migration = scope.Resolve<V8ToV9VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(appId, ctx);
        Assert.That(after!.Grant.PermissionSet?.HasKey(PermissionKeys.ManageContacts), Is.True,
            "a drives-only app with contact-drive write access should still be granted ManageContacts");
    }

    [Test]
    public async Task V9_IsIdempotent()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();

        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();
        var migration = scope.Resolve<V8ToV9VersionMigrationService>();

        await migration.UpgradeAsync(ctx, CancellationToken.None);
        // Second run must be a no-op (already has the key) and still validate.
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(appId, ctx);
        Assert.That(after!.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts), Is.True);
    }

    [Test]
    public async Task V9_GrantsStickerDriveReadWrite_ToSystemApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        // A system app (Chat) that predates the app-level StickerDrive grant. It holds an unrelated
        // drive but no StickerDrive grant.
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
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var before = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(HasStickerDriveReadWrite(before!), Is.False,
            "precondition: app should not yet have the StickerDrive grant");

        var migration = scope.Resolve<V8ToV9VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(HasStickerDriveReadWrite(after!), Is.True,
            "migration should grant StickerDrive ReadWrite to a system app that ships with it");
        // Existing drive grants are preserved.
        Assert.That(after!.Grant.DriveGrants.Any(g =>
            g.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive), Is.True);
    }

    [Test]
    public async Task V9_LeavesNonSystemApp_WithoutStickerDriveGrant()
    {
        var owner = await LoginAsOwner(Identities.Frodo);
        var appId = Guid.NewGuid();

        // A non-system app must not receive the StickerDrive grant, even with contact-drive write
        // access (which only earns ManageContacts).
        await owner.Admin.RegisterApp(appId, new PermissionSetGrantRequest
        {
            Drives = new List<DriveGrantRequest>
            {
                new()
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ContactDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                }
            },
            PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
        });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var migration = scope.Resolve<V8ToV9VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(appId, ctx);
        Assert.That(HasStickerDriveReadWrite(after!), Is.False,
            "a non-system app must not be granted the StickerDrive");
    }

    [Test]
    public async Task V9_BackfillsListsDriveCircleMemberGrant_OnChatApp()
    {
        var owner = await LoginAsOwner(Identities.Frodo);

        // A v8-era Chat app: its circle-member grant covers the ChatDrive but predates the v9 ListsDrive
        // grant that a fresh v9 install ships with (see SystemAppConstants.ChatAppRegistrationRequest).
        await owner.Admin.RegisterApp(
            SystemAppConstants.ChatAppId,
            new PermissionSetGrantRequest
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
                    }
                },
                PermissionSet = new PermissionSet(PermissionKeys.ReadConnections)
            },
            authorizedCircles: new List<Guid>
            {
                SystemCircleConstants.ConfirmedConnectionsCircleId,
                SystemCircleConstants.AutoConnectionsCircleId
            },
            circleMemberGrantRequest: new PermissionSetGrantRequest
            {
                Drives = new List<DriveGrantRequest>
                {
                    new()
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = SystemDriveConstants.ChatDrive,
                            Permission = DrivePermission.Write | DrivePermission.React
                        }
                    }
                },
                PermissionSet = new PermissionSet()
            });

        var scope = Host.GetTenantScope(owner.Identity.DomainName);
        var ctx = await BuildOwnerContextAsync(scope, owner);
        var apps = scope.Resolve<AppRegistrationService>();

        var before = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(HasListsDriveCircleMemberGrant(before!), Is.False,
            "precondition: chat app circle-member grant should not yet include ListsDrive");

        var migration = scope.Resolve<V8ToV9VersionMigrationService>();
        await migration.UpgradeAsync(ctx, CancellationToken.None);
        await migration.ValidateUpgradeAsync(ctx, CancellationToken.None);

        var after = await apps.GetAppRegistration(SystemAppConstants.ChatAppId, ctx);
        Assert.That(HasListsDriveCircleMemberGrant(after!), Is.True,
            "migration should backfill the ListsDrive Write+React circle-member grant onto the chat app");
        // Pre-existing circle-member grants are preserved.
        Assert.That(after!.CircleMemberPermissionSetGrantRequest.Drives.Any(g =>
            g.PermissionedDrive.Drive == SystemDriveConstants.ChatDrive), Is.True,
            "the existing ChatDrive circle-member grant must be preserved");
    }

    private static bool HasListsDriveCircleMemberGrant(RedactedAppRegistration app)
    {
        return app.CircleMemberPermissionSetGrantRequest?.Drives?.Any(g =>
            g.PermissionedDrive.Drive == SystemDriveConstants.ListsDrive &&
            g.PermissionedDrive.Permission.HasFlag(DrivePermission.Write) &&
            g.PermissionedDrive.Permission.HasFlag(DrivePermission.React)) ?? false;
    }

    private static bool HasStickerDriveReadWrite(RedactedAppRegistration app)
    {
        return app.Grant?.DriveGrants?.Any(g =>
            g.PermissionedDrive.Drive == SystemDriveConstants.StickerDrive &&
            g.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite)) ?? false;
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
