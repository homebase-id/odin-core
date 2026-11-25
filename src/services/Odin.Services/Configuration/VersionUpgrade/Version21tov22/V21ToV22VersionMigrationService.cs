using System;
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

namespace Odin.Services.Configuration.VersionUpgrade.Version21tov22
{
    /// <summary>
    /// v21 → v22: the Chat, Feed and Mail apps hold only Read on the <see cref="WellKnownAppDrives.ContactDrive"/>,
    /// and Feed gains <see cref="PermissionKeys.ManageContacts"/> (#1614).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A direct upload or patch of a contact file replaces its content whole and drops the per-app data
    /// <see cref="Odin.Services.Contacts.ContactService"/> keeps.  Contact writes go through that service, which
    /// asserts <see cref="PermissionKeys.ManageContacts"/> and adds Write itself, so the apps need only Read --
    /// the Read grant carries the drive's storage key.  A new identity is registered that way
    /// (<see cref="SystemAppConstants"/>); registration runs only when the app is absent, so a stored grant is
    /// rewritten here.
    /// </para>
    /// <para>
    /// Feed saves the contacts its author names and images show, through the contact API.  v8 → v9 gave it
    /// ManageContacts because it held ContactDrive Write, but an identity made since has neither, so it is added.
    /// </para>
    /// <para>
    /// Every other grant and key is kept as it is.  A revoked app is rewritten too, so it is right if the owner
    /// allows it again, and revoked again after: rebuilding its grant clears the flag.
    /// </para>
    /// </remarks>
    public class V21ToV22VersionMigrationService(
        ILogger<V21ToV22VersionMigrationService> logger,
        AppRegistrationService appRegistrationService)
    {
        private static readonly Guid[] ReadOnlyContactApps =
            [SystemAppConstants.ChatAppId, SystemAppConstants.FeedAppId, SystemAppConstants.MailAppId];

        // ReadWrite carries React and Comment too, and a comment is a write; Read is all a contact needs.
        private const DrivePermission ContactWrites = DrivePermission.Write | DrivePermission.WriteReactionsAndComments;

        public async Task LockContactDriveToReadAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var app in await AppsToCheckAsync(odinContext))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!NeedsUpgrade(app))
                {
                    continue;
                }

                logger.LogInformation("Locking app {appName} ({appId}) to Read on the ContactDrive", app.Name, app.AppId);

                var drives = app.Grant.DriveGrants
                    .Select(g => new DriveGrantRequest { PermissionedDrive = WithoutContactWrites(g.PermissionedDrive) })
                    .ToList();

                var keys = (app.Grant.PermissionSet?.Keys ?? []).ToHashSet();
                if (app.AppId == SystemAppConstants.FeedAppId)
                {
                    keys.Add(PermissionKeys.ManageContacts);
                }

                await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
                {
                    AppId = app.AppId,
                    PermissionSet = new PermissionSet(keys),
                    Drives = drives
                }, odinContext);

                // The rebuilt grant is not revoked; an app the owner revoked stays revoked, and is right when allowed again.
                if (app.IsRevoked)
                {
                    await appRegistrationService.RevokeAppAsync(app.AppId, odinContext);
                }
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            foreach (var app in await AppsToCheckAsync(odinContext))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (NeedsUpgrade(app))
                {
                    throw new OdinSystemException(
                        $"App {app.Name} ({app.AppId}) holds more than Read on the ContactDrive, or Feed lacks ManageContacts");
                }
            }
        }

        private async Task<IEnumerable<RedactedAppRegistration>> AppsToCheckAsync(IOdinContext odinContext)
        {
            var apps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            return apps.Where(a => ReadOnlyContactApps.Contains(a.AppId));
        }

        private static bool NeedsUpgrade(RedactedAppRegistration app)
        {
            var grantsMoreThanRead = app.Grant.DriveGrants.Any(g =>
                g.PermissionedDrive.Drive == WellKnownAppDrives.ContactDrive &&
                (g.PermissionedDrive.Permission & ContactWrites) != 0);

            var feedLacksManageContacts = app.AppId == SystemAppConstants.FeedAppId &&
                                          app.Grant.PermissionSet?.HasKey(PermissionKeys.ManageContacts) != true;

            return grantsMoreThanRead || feedLacksManageContacts;
        }

        /// <summary>The ContactDrive grant without its writes, everything else on it kept; any other grant as it is.</summary>
        private static PermissionedDrive WithoutContactWrites(PermissionedDrive grant)
        {
            if (grant.Drive != WellKnownAppDrives.ContactDrive)
            {
                return grant;
            }

            var readOnly = grant.Clone();
            readOnly.Permission &= ~ContactWrites;
            return readOnly;
        }
    }
}
