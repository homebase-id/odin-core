using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

namespace Odin.Services.Configuration.VersionUpgrade.Version10tov11
{
    /// <summary>
    /// v10 → v11: grants the Chat app (<see cref="SystemAppConstants.ChatAppId"/>)
    /// <see cref="DrivePermission.Write"/> on the <see cref="SystemDriveConstants.ProfileDrive"/> and the
    /// <see cref="PermissionKeys.ManageProfile"/> permission key.
    ///
    /// <para>
    /// A fresh v11 install already ships with both — <see cref="SystemAppConstants"/> now lists the
    /// ProfileDrive with <see cref="DrivePermission.ReadWrite"/> in the Chat app's
    /// <see cref="AppRegistrationRequest.Drives"/> (it previously held only Read) and includes
    /// <see cref="PermissionKeys.ManageProfile"/> in its permission set. This migration backfills the same
    /// upgrade onto <b>existing</b> installs whose stored Chat app grant predates it, preserving every
    /// other drive grant and permission key verbatim and only adding the missing ProfileDrive Write
    /// permission and the ManageProfile key. ManageProfile is what lets profile-attribute writes funnel
    /// through the Profile attribute API once direct ProfileDrive write access is removed.
    /// </para>
    /// </summary>
    public class V10ToV11VersionMigrationService(
        ILogger<V10ToV11VersionMigrationService> logger,
        AppRegistrationService appRegistrationService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (app.AppId != SystemAppConstants.ChatAppId)
                {
                    continue;
                }

                // UpdateAppPermissionsAsync rebuilds the exchange grant, which would reset IsRevoked
                // to false — never touch a revoked app.
                if (app.IsRevoked)
                {
                    logger.LogDebug("Chat app {appName} is revoked; skipping ProfileDrive write backfill", app.Name);
                    continue;
                }

                if (HasProfileDriveWrite(app) && HasManageProfile(app))
                {
                    continue;
                }

                logger.LogInformation(
                    "Granting Write on the ProfileDrive and the ManageProfile key to the Chat app {appName} ({appId})",
                    app.Name, app.AppId);

                // Preserve the app's existing drive grants verbatim — only upgrade the ProfileDrive grant.
                var drives = app.Grant.DriveGrants
                    .Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
                    .ToList();

                // Drop the pre-existing (read-only) ProfileDrive grant so we don't emit a duplicate, then
                // re-add it with ReadWrite.
                drives.RemoveAll(d => d.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive);
                drives.Add(new DriveGrantRequest
                {
                    PermissionedDrive = new PermissionedDrive
                    {
                        Drive = SystemDriveConstants.ProfileDrive,
                        Permission = DrivePermission.ReadWrite
                    }
                });

                // Preserve the app's existing permission keys verbatim — only add the missing ManageProfile.
                var permissionKeys = new List<int>(app.Grant.PermissionSet?.Keys ?? new List<int>());
                if (!permissionKeys.Contains(PermissionKeys.ManageProfile))
                {
                    permissionKeys.Add(PermissionKeys.ManageProfile);
                }

                await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
                {
                    AppId = app.AppId,
                    PermissionSet = new PermissionSet(permissionKeys),
                    Drives = drives
                }, odinContext);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            var chatApp = apps.SingleOrDefault(a => a.AppId == SystemAppConstants.ChatAppId && !a.IsRevoked);

            // A revoked or never-installed Chat app has nothing to validate.
            if (chatApp == null)
            {
                return;
            }

            if (!HasProfileDriveWrite(chatApp))
            {
                throw new OdinSystemException(
                    $"Chat app {chatApp.Name} ({chatApp.AppId}) was not granted Write access to the ProfileDrive");
            }

            if (!HasManageProfile(chatApp))
            {
                throw new OdinSystemException(
                    $"Chat app {chatApp.Name} ({chatApp.AppId}) was not granted the ManageProfile permission");
            }
        }

        private static bool HasProfileDriveWrite(RedactedAppRegistration app)
        {
            return app.Grant?.DriveGrants?.Any(g =>
                g.PermissionedDrive.Drive == SystemDriveConstants.ProfileDrive &&
                g.PermissionedDrive.Permission.HasFlag(DrivePermission.Write)) ?? false;
        }

        private static bool HasManageProfile(RedactedAppRegistration app)
        {
            return app.Grant?.PermissionSet?.Keys?.Contains(PermissionKeys.ManageProfile) ?? false;
        }
    }
}
