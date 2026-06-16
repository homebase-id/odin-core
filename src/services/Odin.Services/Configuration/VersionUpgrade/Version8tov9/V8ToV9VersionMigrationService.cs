using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version8tov9
{
    /// <summary>
    /// v8 → v9: introduces the server-side Contact API. Contact writes now funnel through
    /// <c>/api/v2/contacts</c>, which requires the new <see cref="PermissionKeys.ManageContacts"/>
    /// app permission. This migration grants <c>ManageContacts</c> to every already-registered app
    /// that currently holds <b>write</b> access to the <see cref="SystemDriveConstants.ContactDrive"/>
    /// (today: the Chat and Mail apps), so existing installs keep working after the API ships.
    ///
    /// <para>
    /// v9 also adds two new system drives. The <see cref="SystemDriveConstants.StickerDrive"/> gets an
    /// app-level <see cref="DrivePermission.ReadWrite"/> grant on the system apps that ship with it
    /// (Chat, Feed, Mail). The <see cref="SystemDriveConstants.ListsDrive"/> is granted exactly like the
    /// ChatDrive: app-level ReadWrite on the Chat app, and Write+React to members of the system
    /// connection circles. This migration backfills those grants onto existing installs.
    /// </para>
    ///
    /// <para>
    /// Because the ListsDrive grant flows through the system connection circles, existing connected
    /// identities must be re-granted for the new drive grant to be issued — mirroring how v7 → v8
    /// propagated the Moments drive. App existing drive grants are otherwise preserved verbatim; the
    /// migration only <i>adds</i> what's missing.
    /// </para>
    /// </summary>
    public class V8ToV9VersionMigrationService(
        ILogger<V8ToV9VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleDefinitionService circleDefinitionService,
        AppRegistrationService appRegistrationService,
        IdentityDatabase db)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            // The v9 system drives (Sticker, Lists) are created up front by the EnsureSystemDrivesExist
            // pre-pass in VersionUpgradeService, so they exist before we grant them below.

            // 1. App-level permissions: ManageContacts + the new system-drive grants (Sticker, Lists).
            await UpgradeAppPermissionsAsync(odinContext, cancellationToken);

            // 2. Reconcile the system circle definitions so they include the new ListsDrive grant.
            logger.LogDebug("Updating system circle definitions");
            await circleDefinitionService.EnsureSystemCirclesExistAsync();

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Re-grant connected identities so the ListsDrive circle grant is actually issued.
            await EnsureListsDriveIsConfiguredForConnectionCircles(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (app.IsRevoked)
                {
                    continue;
                }

                if (HasContactDriveWriteAccess(app) &&
                    app.Grant.PermissionSet?.HasKey(PermissionKeys.ManageContacts) != true)
                {
                    throw new OdinSystemException(
                        $"App {app.Name} ({app.AppId}) has ContactDrive write access but was not granted ManageContacts");
                }

                foreach (var drive in RequiredSystemDriveGrants(app))
                {
                    if (!HasDriveReadWrite(app, drive))
                    {
                        throw new OdinSystemException(
                            $"App {app.Name} ({app.AppId}) should have ReadWrite access to drive {drive.Alias} but was not granted it");
                    }
                }
            }

            // Every connected identity who is a member of a system circle must now hold the ListsDrive grant.
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);
            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var circleId in SystemCircleConstants.AllSystemCircles)
                {
                    if (!identity.AccessGrant.CircleGrants.TryGetValue(circleId, out var circleGrant))
                    {
                        continue;
                    }

                    var driveGrant = circleGrant.KeyStoreKeyEncryptedDriveGrants
                        .SingleOrDefault(g => g.PermissionedDrive.Drive == SystemDriveConstants.ListsDrive);

                    if (driveGrant == null)
                    {
                        throw new OdinSystemException("Drive grant for ListsDrive not found");
                    }

                    if (!driveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.Write))
                    {
                        throw new OdinSystemException("ListsDrive not granted write permission");
                    }

                    if (!driveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.React))
                    {
                        throw new OdinSystemException("ListsDrive not granted react permission");
                    }
                }
            }
        }

        private async Task UpgradeAppPermissionsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // UpdateAppPermissionsAsync rebuilds the exchange grant, which would reset IsRevoked
                // to false — never touch a revoked app.
                if (app.IsRevoked)
                {
                    logger.LogDebug("App {appName} is revoked; skipping", app.Name);
                    continue;
                }

                // PermissionSet can legally be null for drives-only registrations.
                var needsManageContacts = HasContactDriveWriteAccess(app) &&
                                          app.Grant.PermissionSet?.HasKey(PermissionKeys.ManageContacts) != true;

                var missingDriveGrants = RequiredSystemDriveGrants(app)
                    .Where(drive => !HasDriveReadWrite(app, drive))
                    .ToList();

                if (!needsManageContacts && missingDriveGrants.Count == 0)
                {
                    continue;
                }

                // Preserve the app's existing drive grants verbatim — only add what's missing.
                var drives = app.Grant.DriveGrants
                    .Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
                    .ToList();

                foreach (var drive in missingDriveGrants)
                {
                    logger.LogInformation(
                        "Granting ReadWrite on drive {drive} to app {appName} ({appId})", drive.Alias, app.Name, app.AppId);

                    // Drop any pre-existing (lesser) grant for this drive so we don't emit a duplicate.
                    drives.RemoveAll(d => d.PermissionedDrive.Drive == drive);
                    drives.Add(new DriveGrantRequest
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = drive,
                            Permission = DrivePermission.ReadWrite
                        }
                    });
                }

                var keys = new List<int>(app.Grant.PermissionSet?.Keys ?? new List<int>());
                if (needsManageContacts)
                {
                    logger.LogInformation(
                        "Granting ManageContacts to app {appName} ({appId}) — it has write access to the ContactDrive",
                        app.Name, app.AppId);
                    keys.Add(PermissionKeys.ManageContacts);
                }

                await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
                {
                    AppId = app.AppId,
                    PermissionSet = new PermissionSet(keys),
                    Drives = drives
                }, odinContext);
            }
        }

        private async Task EnsureListsDriveIsConfiguredForConnectionCircles(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            await using var tx = await db.BeginStackedTransactionAsync(IsolationLevel.Unspecified, cancellationToken);

            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Identities whose access grant was established without the owner's master key
                // (e.g. introduction-based connections) have no MasterKeyEncryptedKeyStoreKey, so
                // re-granting would dereference a null key. We have the master key here, so attempt
                // the same upgrade the circle-definition reconcile path performs. If it still can't be
                // upgraded, skip it rather than crash the batch; it will be reconciled later once the
                // identity completes its upgrade.
                if (identity.AccessGrant.RequiresMasterKeyEncryptionUpgrade())
                {
                    var upgraded = await circleNetworkService.TryUpgradeMasterKeyStoreKeyEncryptionAsync(identity, odinContext);
                    if (!upgraded)
                    {
                        logger.LogWarning(
                            "Skipping system circle reconciliation for Identity {odinId}: access grant still requires master key encryption upgrade",
                            identity.OdinId);
                        continue;
                    }
                }

                // Re-grant whichever system circles this identity is a member of so the new ListsDrive
                // grant is issued to confirmed and auto-connected identities alike.
                foreach (var circleId in SystemCircleConstants.AllSystemCircles)
                {
                    if (!identity.AccessGrant.CircleGrants.ContainsKey(circleId))
                    {
                        continue;
                    }

                    logger.LogDebug("Reconciling system circle {circleId} on Identity {odinId}", circleId, identity.OdinId);

                    await circleNetworkService.RevokeCircleAccessAsync(circleId, identity.OdinId, odinContext);
                    await circleNetworkService.GrantCircleAsync(circleId, identity.OdinId, odinContext);
                }
            }

            var allApps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in allApps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext);
            }

            tx.Commit();
        }

        // The app-level system-drive grants introduced in v9, by app. All are granted ReadWrite.
        private static TargetDrive[] RequiredSystemDriveGrants(RedactedAppRegistration app)
        {
            if (app.AppId == SystemAppConstants.ChatAppId)
            {
                return [SystemDriveConstants.StickerDrive, SystemDriveConstants.ListsDrive];
            }

            if (app.AppId == SystemAppConstants.FeedAppId || app.AppId == SystemAppConstants.MailAppId)
            {
                return [SystemDriveConstants.StickerDrive];
            }

            return [];
        }

        private static bool HasContactDriveWriteAccess(RedactedAppRegistration app)
        {
            return app.Grant?.DriveGrants?.Any(g =>
                g.PermissionedDrive.Drive == SystemDriveConstants.ContactDrive &&
                g.PermissionedDrive.Permission.HasFlag(DrivePermission.Write)) ?? false;
        }

        private static bool HasDriveReadWrite(RedactedAppRegistration app, TargetDrive drive)
        {
            return app.Grant?.DriveGrants?.Any(g =>
                g.PermissionedDrive.Drive == drive &&
                g.PermissionedDrive.Permission.HasFlag(DrivePermission.ReadWrite)) ?? false;
        }
    }
}
