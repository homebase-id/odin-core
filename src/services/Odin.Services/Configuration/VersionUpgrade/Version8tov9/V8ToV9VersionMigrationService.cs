using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;

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
    /// The app's existing drive grants are preserved verbatim — this migration only <i>adds</i> a
    /// permission key. We do not downgrade the ContactDrive grant from ReadWrite to Read here (see the
    /// upgrade notes doc); that is a separate, riskier decision tied to the client migration.
    /// </para>
    /// </summary>
    public class V8ToV9VersionMigrationService(
        ILogger<V8ToV9VersionMigrationService> logger,
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

                if (!HasContactDriveWriteAccess(app))
                {
                    continue;
                }

                if (app.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts))
                {
                    logger.LogDebug("App {appName} already has ManageContacts; skipping", app.Name);
                    continue;
                }

                logger.LogInformation(
                    "Granting ManageContacts to app {appName} ({appId}) — it has write access to the ContactDrive",
                    app.Name, app.AppId);

                var keys = new List<int>(app.Grant.PermissionSet.Keys ?? new List<int>())
                {
                    PermissionKeys.ManageContacts
                };

                // Preserve the app's existing drive grants verbatim — only add the permission key.
                var drives = app.Grant.DriveGrants
                    .Select(g => new DriveGrantRequest { PermissionedDrive = g.PermissionedDrive })
                    .ToList();

                await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
                {
                    AppId = app.AppId,
                    PermissionSet = new PermissionSet(keys),
                    Drives = drives
                }, odinContext);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in apps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!HasContactDriveWriteAccess(app))
                {
                    continue;
                }

                if (!app.Grant.PermissionSet.HasKey(PermissionKeys.ManageContacts))
                {
                    throw new OdinSystemException(
                        $"App {app.Name} ({app.AppId}) has ContactDrive write access but was not granted ManageContacts");
                }
            }
        }

        private static bool HasContactDriveWriteAccess(RedactedAppRegistration app)
        {
            return app.Grant?.DriveGrants?.Any(g =>
                g.PermissionedDrive.Drive == SystemDriveConstants.ContactDrive &&
                g.PermissionedDrive.Permission.HasFlag(DrivePermission.Write)) ?? false;
        }
    }
}
