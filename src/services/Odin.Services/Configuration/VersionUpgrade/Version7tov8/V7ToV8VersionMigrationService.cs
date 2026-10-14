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
        IdentityDatabase db,
        IDriveManager driveManager)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogDebug("Ensuring system drives exist on identity: [{identity}]", odinContext.Tenant);
            await tenantConfigService.EnsureSystemDrivesExist(odinContext);

            cancellationToken.ThrowIfCancellationRequested();

            await ReconcileConnectionsAsync(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var momentsDrive = await driveManager.GetDriveAsync(WellKnownAppDrives.MomentsDrive.Alias, false);
            if (momentsDrive == null)
            {
                throw new OdinSystemException("Moments drive not created");
            }
        }

        /// <remarks>
        /// This release also re-granted the Confirmed and Auto Connections system circles so their members
        /// picked up Write + React on the Moments drive.  Both circles are retired (#1809) and V19 -&gt; V20
        /// deletes them, so that part is gone; the key upgrade and the app grant reconcile remain.
        /// </remarks>
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
                // If it still can't be upgraded (e.g. no TempWeakKeyStoreKey to recover from), it is left for
                // later rather than crashing the batch.
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
    }
}
