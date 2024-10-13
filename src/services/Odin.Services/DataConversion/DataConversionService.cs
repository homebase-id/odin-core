using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Verification;

namespace Odin.Services.DataConversion
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class DataConversionService(
        ILogger<DataConversionService> logger,
        IAppRegistrationService appRegistrationService,
        TenantSystemStorage tenantSystemStorage,
        CircleDefinitionService circleDefinitionService,
        CircleNetworkService circleNetworkService,
        CircleNetworkVerificationService verificationService,
        PublicPrivateKeyService publicPrivateKeyService)
    {
        public async Task AutoFixCircleGrants(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var db = tenantSystemStorage.IdentityDatabase;
            var allIdentities = await circleNetworkService.GetConnectedIdentities(int.MaxValue, 0, odinContext, db);

            //TODO CONNECTIONS
            // await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                foreach (var identity in allIdentities.Results)
                {
                    await FixIdentity(identity, odinContext, db);
                }

                var allApps = await appRegistrationService.GetRegisteredApps(odinContext, db);
                foreach (var app in allApps)
                {
                    logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                    await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext, db);
                }
            }
            //);
        }

        /// <summary>
        /// Handles the changes to production data required for the introductions feature
        /// </summary>
        public async Task PrepareIntroductionsRelease(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            var db = tenantSystemStorage.IdentityDatabase;

            //
            // Generate new Online Icr Encrypted ECC Key
            //
            await publicPrivateKeyService.CreateInitialKeys(odinContext, db);
            
            //
            // Create new circles
            //
            await circleDefinitionService.EnsureSystemCirclesExist();

            var allIdentities = await circleNetworkService.GetConnectedIdentities(int.MaxValue, 0, odinContext, db);
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
                        var success = await verificationService.SynchronizeVerificationHash(identity.OdinId, odinContext, db);
                        logger.LogDebug("EnsureVerificationHash for {odinId}.  Succeeded: {success}", identity.OdinId, success);
                    }
                }
            }
            //);
        }


        private async Task FixIdentity(IdentityConnectionRegistration icr, IOdinContext odinContext, IdentityDatabase db)
        {
            foreach (var circleGrant in icr.AccessGrant.CircleGrants)
            {
                var circleId = circleGrant.Value.CircleId;

                var def = circleDefinitionService.GetCircle(circleId);
                logger.LogDebug("Fixing Identity {odinId} in {circle}", icr.OdinId, def.Name);

                await circleNetworkService.RevokeCircleAccess(circleId, icr.OdinId, odinContext, db);
                await circleNetworkService.GrantCircle(circleId, icr.OdinId, odinContext, db);
            }
        }
    }
}