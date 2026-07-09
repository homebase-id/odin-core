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

            //
            // Update the confirmed-connections circle definition so it grants Write + React on the Moments drive
            //
            logger.LogDebug("Updating system circle definitions");
            await circleDefinitionService.EnsureSystemCirclesExistAsync();

            cancellationToken.ThrowIfCancellationRequested();

            await EnsureMomentsDriveIsConfiguredForConnectionCircles(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var momentsDrive = await driveManager.GetDriveAsync(SystemDriveConstants.MomentsDrive.Alias, false);
            if (momentsDrive == null)
            {
                throw new OdinSystemException("Moments drive not created");
            }

            // Get all ICRs and ensure the confirmed/auto connection circles have Write + React access to my Moments drive
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var circleId in SystemCircleConstants.AllSystemCircles)
                {
                    // Only validate the identities who are members of this system circle
                    if (!identity.PeerKeyStore.CircleGrants.TryGetValue(circleId, out var circleGrant))
                    {
                        continue;
                    }

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

        private async Task EnsureMomentsDriveIsConfiguredForConnectionCircles(IOdinContext odinContext,
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
                // upgraded (e.g. no TempWeakKeyStoreKey to recover from), skip it rather than crash the
                // batch; it will be reconciled later once the identity completes its upgrade.
                if (identity.PeerKeyStore.RequiresMasterKeyEncryptionUpgrade())
                {
                    var upgraded = await circleNetworkService.TryUpgradeMasterKeyStoreKeyEncryptionAsync(identity, odinContext);
                    if (!upgraded)
                    {
                        logger.LogWarning(
                            "Skipping system circle reconciliation for Identity  {odinId}: access grant still requires master key encryption upgrade",
                            identity.OdinId);
                        continue;
                    }
                }

                // Re-grant whichever system circles this identity is a member of so the new
                // Moments drive grant is issued to confirmed and auto-connected identities alike
                foreach (var circleId in SystemCircleConstants.AllSystemCircles)
                {
                    if (!identity.PeerKeyStore.CircleGrants.ContainsKey(circleId))
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
    }
}
