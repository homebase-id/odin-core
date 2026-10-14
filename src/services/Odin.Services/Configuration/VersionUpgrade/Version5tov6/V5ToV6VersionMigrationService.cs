using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version5tov6
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V5ToV6VersionMigrationService(
        ILogger<V5ToV6VersionMigrationService> logger,
        TenantConfigService tenantConfigService,
        CircleNetworkService circleNetworkService,
        AppRegistrationService appRegistrationService,
        IdentityDatabase db,
        IDriveManager driveManager)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            logger.LogDebug("Preparing Shamira release 1 on identity: [{identity}]", odinContext.Tenant);
            await tenantConfigService.EnsureSystemDrivesExist(odinContext);

            cancellationToken.ThrowIfCancellationRequested();

            await ReconcileAppCircleGrantsAsync(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var shardDrive = await driveManager.GetDriveAsync(WellKnownAppDrives.ShardRecoveryDrive.Alias, false);

            if (shardDrive == null)
            {
                throw new OdinSystemException("Shard recovery drive not created");
            }
        }

        /// <remarks>
        /// This release also re-granted the Confirmed Connections circle so its members picked up Write on the
        /// Shard Recovery drive.  That circle is retired (#1809) and V19 -&gt; V20 deletes it, so only the app
        /// grant reconcile is left.
        /// </remarks>
        private async Task ReconcileAppCircleGrantsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            await using var tx = await db.BeginStackedTransactionAsync(IsolationLevel.Unspecified, cancellationToken);

            var allApps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in allApps)
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext);
            }

            tx.Commit();
        }
    }
}