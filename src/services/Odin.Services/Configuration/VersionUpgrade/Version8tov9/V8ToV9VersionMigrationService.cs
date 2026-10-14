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
using Odin.Services.Apps.Builtin;
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
    /// that currently holds <b>write</b> access to the <see cref="WellKnownAppDrives.ContactDrive"/>
    /// (today: the Chat and Mail apps), so existing installs keep working after the API ships.
    ///
    /// <para>
    /// v9 also adds new system drives. The <see cref="WellKnownAppDrives.StickerDrive"/> gets an
    /// app-level <see cref="DrivePermission.ReadWrite"/> grant on the system apps that ship with it
    /// (Chat, Feed, Mail). The <see cref="WellKnownAppDrives.LocationDrive"/> gets app-level ReadWrite
    /// on the Chat app. The <see cref="WellKnownAppDrives.ListsDrive"/> is granted exactly like the
    /// ChatDrive: app-level ReadWrite on the Chat app, and Write+React to circle members through the Chat
    /// app's <see cref="AppRegistrationRequest.CircleMemberPermissionGrant"/>, which a fresh v9 install
    /// ships with but an upgraded v8 install does not. The migration rewrites the stored grant to add the
    /// missing ListsDrive grant and re-issues the resulting app circle grant to every member of the app's
    /// authorized circles.
    /// </para>
    ///
    /// <para>
    /// This release also added the ListsDrive grant to the Confirmed and Auto Connections system circles
    /// and re-granted their members.  Both circles are retired (#1809) and V19 → V20 deletes them, so that
    /// part, and its validation, is gone.
    /// </para>
    ///
    /// <para>
    /// App existing drive grants (both app-level and circle-member) are otherwise preserved verbatim;
    /// the migration only <i>adds</i> what's missing.
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

            // 1b. Backfill the new v9 circle-member drive grants onto the system apps' stored
            // CircleMemberPermissionGrant (today: the Chat app's ListsDrive Write+React grant to
            // connection-circle members). UpdateAppPermissionsAsync above only rewrites the app's own
            // grant, so without this an upgraded install never receives the circle-member grant that a
            // fresh v9 install ships with.
            await UpgradeCircleMemberGrantsAsync(odinContext, cancellationToken);

            // 2. v9 is where Emergency Location Access arrives.  It is created here from the tree, which is
            // what a v13 identity gets and carries the owning app with it.
            await circleDefinitionService.EnsureCircleExistsAsync(BuiltinCircles.EmergencyLocationAccessCircle);

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Upgrade connection key encryption where needed and re-issue app circle grants.
            await ReconcileConnectionsAsync(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The new v9 Emergency Location Access built-in circle definition must have been provisioned
            // by the upgrade above. It ships empty (no members), so there's nothing to backfill onto
            // connected identities — only the definition itself needs to exist.
            var emergencyLocationCircleId = BuiltinCircles.EmergencyLocationAccessCircle.Id;
            var emergencyLocationCircle = await circleDefinitionService.GetCircleAsync(emergencyLocationCircleId);
            if (emergencyLocationCircle == null)
            {
                throw new OdinSystemException(
                    $"Built-in circle {emergencyLocationCircleId} (Emergency Location Access) was not created");
            }

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

                // The new v9 circle-member drive grants must be present on the stored CircleMemberPermissionGrant.
                foreach (var grant in RequiredCircleMemberDriveGrants(app))
                {
                    var hasGrant = app.CircleMemberPermissionSetGrantRequest?.Drives?.Any(d =>
                        d.PermissionedDrive.Drive == grant.PermissionedDrive.Drive &&
                        d.PermissionedDrive.Permission.HasFlag(grant.PermissionedDrive.Permission)) ?? false;

                    if (!hasGrant)
                    {
                        throw new OdinSystemException(
                            $"App {app.Name} ({app.AppId}) is missing circle-member grant " +
                            $"{grant.PermissionedDrive.Permission} on drive {grant.PermissionedDrive.Drive.Alias}");
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

        private async Task UpgradeCircleMemberGrantsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Mirror UpgradeAppPermissionsAsync: re-issuing the grant would resurrect a revoked app.
                if (app.IsRevoked)
                {
                    logger.LogDebug("App {appName} is revoked; skipping circle-member grant upgrade", app.Name);
                    continue;
                }

                var required = RequiredCircleMemberDriveGrants(app);
                if (required.Length == 0)
                {
                    continue;
                }

                // Preserve the app's existing circle-member drive grants verbatim — only add what's missing.
                var drives = (app.CircleMemberPermissionSetGrantRequest?.Drives ?? new List<DriveGrantRequest>())
                    .Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
                    .ToList();

                var missing = required
                    .Where(r => !drives.Any(d =>
                        d.PermissionedDrive.Drive == r.PermissionedDrive.Drive &&
                        d.PermissionedDrive.Permission.HasFlag(r.PermissionedDrive.Permission)))
                    .ToList();

                if (missing.Count == 0)
                {
                    continue;
                }

                foreach (var grant in missing)
                {
                    logger.LogInformation(
                        "Granting circle-member {permission} on drive {drive} to app {appName} ({appId})",
                        grant.PermissionedDrive.Permission, grant.PermissionedDrive.Drive.Alias, app.Name, app.AppId);

                    // Drop any pre-existing (lesser) grant for this drive so we don't emit a duplicate.
                    drives.RemoveAll(d => d.PermissionedDrive.Drive == grant.PermissionedDrive.Drive);
                    drives.Add(grant);
                }

                // UpdateAuthorizedCirclesAsync persists the new CircleMemberPermissionGrant and, via the
                // AppRegistrationChangedNotification it publishes, re-issues the app circle grant to every
                // member of the authorized circles. The subsequent ReconcileAuthorizedCircles pass in
                // EnsureListsDriveIsConfiguredForConnectionCircles re-applies it as a backstop.
                await appRegistrationService.UpdateAuthorizedCirclesAsync(new UpdateAuthorizedCirclesRequest
                {
                    AppId = app.AppId,
                    AuthorizedCircles = new List<Guid>(app.AuthorizedCircles ?? new List<Guid>()),
                    CircleMemberPermissionGrant = new PermissionSetGrantRequest
                    {
                        Drives = drives,
                        PermissionSet = app.CircleMemberPermissionSetGrantRequest?.PermissionSet ?? new PermissionSet()
                    }
                }, odinContext);
            }
        }

        private async Task ReconcileConnectionsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            await using var tx = await db.BeginStackedTransactionAsync(IsolationLevel.Unspecified, cancellationToken);

            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Identities whose access grant was established without the owner's master key
                // (e.g. introduction-based connections) have no MasterKeyEncryptedKeyStoreKey.  We have the
                // master key here, so attempt the same upgrade the circle-definition reconcile path performs.
                // If it still can't be upgraded, it is left for later rather than crashing the batch.
                if (identity.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                {
                    var upgraded = await circleNetworkService.TryUpgradeMasterKeyStoreKeyEncryptionAsync(identity, odinContext);
                    if (!upgraded)
                    {
                        logger.LogWarning(
                            "Identity {odinId}: access grant still requires master key encryption upgrade",
                            identity.OdinId);
                    }
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
                return [WellKnownAppDrives.StickerDrive, WellKnownAppDrives.ListsDrive, WellKnownAppDrives.LocationDrive];
            }

            if (app.AppId == SystemAppConstants.FeedAppId || app.AppId == SystemAppConstants.MailAppId)
            {
                return [WellKnownAppDrives.StickerDrive];
            }

            return [];
        }

        // The circle-member drive grants introduced in v9, by app. Members of the app's authorized
        // (connection) circles receive these through the app's CircleMemberPermissionGrant, mirroring
        // SystemAppConstants. Today only the Chat app gains a grant (ListsDrive Write+React).
        private static DriveGrantRequest[] RequiredCircleMemberDriveGrants(RedactedAppRegistration app)
        {
            if (app.AppId == SystemAppConstants.ChatAppId)
            {
                return
                [
                    new DriveGrantRequest
                    {
                        PermissionedDrive = new PermissionedDrive
                        {
                            Drive = WellKnownAppDrives.ListsDrive,
                            Permission = DrivePermission.Write | DrivePermission.React
                        }
                    }
                ];
            }

            return [];
        }

        private static bool HasContactDriveWriteAccess(RedactedAppRegistration app)
        {
            return app.Grant?.DriveGrants?.Any(g =>
                g.PermissionedDrive.Drive == WellKnownAppDrives.ContactDrive &&
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
