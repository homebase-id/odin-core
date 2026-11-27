using System;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade.Version12tov13;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Requests;

namespace Odin.Services.Authorization.Apps;

#nullable enable

/// <summary>
/// The owner's full removal of an app (#1870), third-party or an optional known one (#2138): its clients and their
/// push subscriptions, its grants on every connection, and -- when the owner says so -- the circles and drives it
/// owns. An app that owns any is refused unless they go with it.  A provisioned one is remembered as uninstalled, so
/// an upgrade doesn't bring it back.
/// </summary>
/// <remarks>
/// Runs in steps rather than one transaction, because a drive's files are removed after its own transaction
/// commits. The order makes a failure re-runnable: owned drives, then circles and connections, then clients,
/// and the registration last -- so until the last step the app is still there to uninstall again.
/// </remarks>
public class AppUninstallService(
    AppRegistrationService appRegistrationService,
    CircleNetworkService circleNetworkService,
    CircleNetworkRequestService circleNetworkRequestService,
    CircleDefinitionService circleDefinitionService,
    DriveManager driveManager,
    DriveDeletionService driveDeletionService,
    ClientRegistrationStorage clientRegistrationStorage,
    PushNotificationService pushNotificationService,
    LegacyDefinitionStore legacyStore,
    UninstalledBuiltinApps uninstalledApps,
    IdentityDatabase db)
{
    /// <summary>
    /// The owner console and the core of the platform: never uninstalled, only revoked.  An optional known app is
    /// not, even when the platform provisions it (<see cref="BuiltinApps.IsUninstallable"/>).
    /// </summary>
    public static bool IsBuiltIn(Guid appId) => SystemAppConstants.IsOwnerConsole(appId) ||
                                               (BuiltinApps.IsPlatformApp(appId) && !BuiltinApps.IsUninstallable(appId));

    public async Task UninstallAsync(Guid appId, bool deleteOwnedCirclesAndDrives, IOdinContext odinContext)
    {
        odinContext.Caller.AssertHasMasterKey();

        if (await appRegistrationService.GetAppRegistration(appId, odinContext) == null)
        {
            throw new OdinClientException("Invalid App Id", OdinClientErrorCode.AppNotRegistered);
        }

        if (IsBuiltIn(appId))
        {
            throw new OdinClientException("A built-in app cannot be uninstalled");
        }

        // Pre-v13 a registration also lives in a blob row the version move would bring back.
        if (await legacyStore.IsPreMoveAsync())
        {
            throw new OdinClientException("Apps cannot be uninstalled until this identity's upgrade has finished");
        }

        var drives = await driveManager.GetDrivesByAppIdAsync(appId, odinContext);
        var circleIds = (await circleDefinitionService.GetCirclesAsync())
            .Where(c => c.AppId == appId)
            .Select(c => c.Id.Value)
            .ToList();

        if ((drives.Count > 0 || circleIds.Count > 0) && !deleteOwnedCirclesAndDrives)
        {
            throw new OdinClientException(
                $"The app owns {drives.Count} drive(s) and {circleIds.Count} circle(s); uninstalling it deletes them, " +
                "so that has to be asked for");
        }

        // Before anything is removed: a circle that cannot be deleted must not first lose its grants everywhere.
        foreach (var circleId in circleIds)
        {
            await circleDefinitionService.AssertDeletableAsync(circleId, appId);
        }

        // Each delete also takes every other app's, circle's and connection's grant on the drive.  A drive the owner may
        // archive is archived first, which tells whoever follows its definition (the home page's cache of a channel
        // drive); a system drive can't be, and none of an uninstallable app's is a channel drive.
        foreach (var drive in drives)
        {
            if (!BuiltinDrives.IsProtected(drive.Id))
            {
                await driveManager.SetArchiveDriveFlagAsync(drive.Id, true, odinContext);
            }

            await driveDeletionService.DeleteDriveAsync(drive.Id, appId, odinContext);
        }

        await appRegistrationService.RemoveCirclesFromAllAppsAsync(circleIds, odinContext);
        await circleNetworkService.RemoveAppFromAllConnectionsAsync(appId, circleIds, odinContext);
        await circleNetworkRequestService.RemoveAppFromSentRequestsAsync(appId, circleIds, odinContext);

        // Every client, live or expired, and the push subscription each may hold.
        var clientIds = (await db.ClientRegistrations.GetByTypeAndCategoryIdAsync(AppClientRegistration.CatType, appId))
            .Select(r => r.catId)
            .ToList();
        foreach (var clientId in clientIds)
        {
            await pushNotificationService.RemoveDeviceAsync(clientId, odinContext);
        }

        await clientRegistrationStorage.DeleteManyAsync(clientIds);

        // With the registration: a provisioned app stays uninstalled across upgrades.  Until here a failure leaves it
        // installed, and a later upgrade gives it back any drive already deleted, once that drive's purge has
        // finished (BuiltinProvisioner.EnsureDrivesAsync leaves it until then), so uninstalling it again is safe.
        await using var tx = await db.BeginStackedTransactionAsync();
        if (BuiltinProvisioner.IsProvisioned(appId))
        {
            await uninstalledApps.AddAsync(appId);
        }

        await appRegistrationService.DeleteRegistrationAsync(appId, odinContext);
        tx.Commit();
    }
}
