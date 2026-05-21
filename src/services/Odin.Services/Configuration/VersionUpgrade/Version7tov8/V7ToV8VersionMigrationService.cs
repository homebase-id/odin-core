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

namespace Odin.Services.Configuration.VersionUpgrade.Version7tov8
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V7ToV8VersionMigrationService(
        ILogger<V7ToV8VersionMigrationService> logger,
        TenantConfigService tenantConfigService,
        CircleNetworkService circleNetworkService,
        AppRegistrationService appRegistrationService,
        CircleDefinitionService circleDefinitionService,
        IdentityDatabase db,
        IDriveManager driveManager)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogDebug("Ensuring system drives exist on identity: [{identity}]", odinContext.Tenant);
            await tenantConfigService.EnsureSystemDrivesExist(odinContext);

            logger.LogDebug("Ensuring system circles exist on identity: [{identity}]", odinContext.Tenant);
            await circleDefinitionService.EnsureSystemCirclesExistAsync();

            cancellationToken.ThrowIfCancellationRequested();

            await EnsureMomentsDriveIsConfiguredForConnectedIdentitiesCircle(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var momentsDrive = await driveManager.GetDriveAsync(SystemDriveConstants.MomentsDrive.Alias, false);
            if (momentsDrive == null)
            {
                throw new OdinSystemException("Moments drive not created");
            }

            // Get all ICRs and ensure they have write access to my Moments Drive
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Only validate the identities who are confirmed connected
                if (identity.AccessGrant.CircleGrants.TryGetValue(SystemCircleConstants.ConfirmedConnectionsCircleId, out var circleGrant))
                {
                    var driveGrant = circleGrant.KeyStoreKeyEncryptedDriveGrants
                        .SingleOrDefault(g => g.PermissionedDrive.Drive == SystemDriveConstants.MomentsDrive);

                    if (driveGrant == null)
                    {
                        throw new OdinSystemException("Drive grant for MomentsDrive not found");
                    }

                    var hasWrite = driveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.Write);
                    if (!hasWrite)
                    {
                        throw new OdinSystemException("MomentsDrive not granted write permission");
                    }

                    var hasReact = driveGrant.PermissionedDrive.Permission.HasFlag(DrivePermission.React);
                    if (!hasReact)
                    {
                        throw new OdinSystemException("MomentsDrive not granted react permission");
                    }
                }
            }
        }

        private async Task EnsureMomentsDriveIsConfiguredForConnectedIdentitiesCircle(IOdinContext odinContext,
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
