using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Verification;

namespace Odin.Services.Configuration.VersionUpgrade.Version0tov1
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V0ToV1VersionMigrationService(
        ILogger<V0ToV1VersionMigrationService> logger,
        IAppRegistrationService appRegistrationService,
        TenantSystemStorage tenantSystemStorage,
        CircleDefinitionService circleDefinitionService,
        CircleNetworkService circleNetworkService,
        CircleNetworkVerificationService verificationService,
        PublicPrivateKeyService publicPrivateKeyService)
    {
        public async Task Upgrade(IOdinContext odinContext)
        {
            await PrepareIntroductionsRelease(odinContext);
            
            await AutoFixCircleGrants(odinContext);

        }

        public async Task AutoFixCircleGrants(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var allIdentities = await circleNetworkService.GetConnectedIdentities(int.MaxValue, 0, odinContext);

            //TODO CONNECTIONS
            // await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                foreach (var identity in allIdentities.Results)
                {
                    await FixIdentity(identity, odinContext);
                }

                var allApps = await appRegistrationService.GetRegisteredApps(odinContext);
                foreach (var app in allApps)
                {
                    logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                    await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext);
                }
            }
            //);
        }

        /// <summary>
        /// Handles the changes to production data required for the introductions feature
        /// </summary>
        private async Task PrepareIntroductionsRelease(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            //
            // Generate new Online Icr Encrypted ECC Key
            //
            await publicPrivateKeyService.CreateInitialKeys(odinContext);

            //
            // Create new circles
            //
            await circleDefinitionService.EnsureSystemCirclesExist();

            var allIdentities = await circleNetworkService.GetConnectedIdentities(int.MaxValue, 0, odinContext);

            //TODO CONNECTIONS
            // await db.CreateCommitUnitOfWorkAsync(async () =>
            {
                foreach (var identity in allIdentities.Results)
                {
                    //
                    // Sync verification hash
                    //
                    if (identity.VerificationHash?.Length == 0)
                    {
                        var success = await verificationService.SynchronizeVerificationHash(identity.OdinId, odinContext);
                        logger.LogDebug("EnsureVerificationHash for {odinId}.  Succeeded: {success}", identity.OdinId, success);
                    }
                }
            }
            //);
        }

        private async Task FixIdentity(IdentityConnectionRegistration icr, IOdinContext odinContext)
        {
            foreach (var circleGrant in icr.AccessGrant.CircleGrants)
            {
                var circleId = circleGrant.Value.CircleId;

                var def = circleDefinitionService.GetCircle(circleId);
                logger.LogDebug("Fixing Identity {odinId} in {circle}", icr.OdinId, def.Name);

                await circleNetworkService.RevokeCircleAccess(circleId, icr.OdinId, odinContext);
                await circleNetworkService.GrantCircle(circleId, icr.OdinId, odinContext);
            }
        }
    }
}