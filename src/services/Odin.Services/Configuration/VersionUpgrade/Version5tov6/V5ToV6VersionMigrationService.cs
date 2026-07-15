using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.Circles;
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
        CircleDefinitionService circleDefinitionService,
        IdentityDatabase db,
        IDriveManager driveManager)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            logger.LogDebug("Preparing Shamira release 1 on identity: [{identity}]", odinContext.Tenant);
            await tenantConfigService.EnsureSystemDrivesExist(odinContext);

            //
            // Create new circles, rename existing ones
            //
            logger.LogDebug("Creating new circles; update existing ones");
            await circleDefinitionService.EnsureSystemCirclesExistAsync();

            cancellationToken.ThrowIfCancellationRequested();

            await EnsureShardRecoveryDriveIsConfiguredForConnectedIdentitiesCircle(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var shardDrive = await driveManager.GetDriveAsync(SystemDriveConstants.ShardRecoveryDrive.Alias, false);

            if (shardDrive == null)
            {
                throw new OdinSystemException("Shard recovery drive not created");
            }

            // Get all ICRs and ensure they have write access to my Shard Recovery Drive
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only validate the identities who are confirmed connected
                if (identity.AccessGrant.CircleGrants.TryGetValue(SystemCircleConstants.ConfirmedConnectionsCircleId, out var circleGrant))
                {
                    var driveGrant = circleGrant.KeyStoreKeyEncryptedDriveGrants
                        .SingleOrDefault(g => g.PermissionedDrive.Drive == SystemDriveConstants.ShardRecoveryDrive);

                    if (driveGrant == null)
                    {
                        throw new OdinSystemException("Drive grant for ShardRecoveryDrive not found");
                    }

                    var hasWrite = driveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.Write);
                    if (!hasWrite)
                    {
                        throw new OdinSystemException("ShardRecoveryDrive not granted write permission");
                    }
                }
            }
        }

        private async Task EnsureShardRecoveryDriveIsConfiguredForConnectedIdentitiesCircle(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            await using var tx = await db.BeginStackedTransactionAsync(IsolationLevel.Unspecified, cancellationToken);

            var circleId = SystemCircleConstants.ConfirmedConnectionsCircleId;
            foreach (var identity in allIdentities.Results.Where(ident => ident.IsConfirmedConnection()))
            {
                cancellationToken.ThrowIfCancellationRequested();

                logger.LogDebug("Reconciling confirmed connections circle on Identity {odinId}", identity.OdinId);

                await circleNetworkService.RevokeCircleAccessAsync(circleId, identity.OdinId, odinContext);
                await circleNetworkService.GrantCircleAsync(circleId, identity.OdinId, odinContext);
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
    }
}