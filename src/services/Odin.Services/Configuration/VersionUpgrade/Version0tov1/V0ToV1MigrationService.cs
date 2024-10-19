using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Apps;
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
        CircleDefinitionService circleDefinitionService,
        CircleNetworkService circleNetworkService,
        CircleNetworkVerificationService verificationService,
        PublicPrivateKeyService publicPrivateKeyService)
    {
        public async Task Upgrade(IOdinContext odinContext)
        {
            logger.LogDebug("Preparing Introductions Release for Identity [{identity}]", odinContext.Tenant);
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
            logger.LogDebug("Creating new Online Icr Encrypted ECC Key");
            await publicPrivateKeyService.CreateInitialKeys(odinContext);

            //
            // Create new circles, rename existing ones
            //
            logger.LogDebug("Creating new circles; renaming existing ones");
            await circleDefinitionService.EnsureSystemCirclesExist();

            //
            // This will reapply the grants since we added a new permission
            //
            logger.LogDebug("Reapplying permissions for ConfirmedConnections Circle");
            await circleNetworkService.UpdateCircleDefinition(SystemCircleConstants.ConfirmedConnectionsDefinition, odinContext);

            //
            // Update the apps that use the new circle
            //
            logger.LogDebug("Updating system apps with new circles and permissions");
            await UpdateApp(SystemAppConstants.ChatAppRegistrationRequest, odinContext);
            await UpdateApp(SystemAppConstants.MailAppRegistrationRequest, odinContext);

            //
            // Sync verification hash's across all connections
            //
            logger.LogInformation("Syncing verification hashes");
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

        private async Task UpdateApp(AppRegistrationRequest request, IOdinContext odinContext)
        {
            await appRegistrationService.UpdateAuthorizedCircles(new UpdateAuthorizedCirclesRequest
            {
                AppId = request.AppId,
                AuthorizedCircles = request.AuthorizedCircles,
                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant
            }, odinContext);

            await appRegistrationService.UpdateAppPermissions(new UpdateAppPermissionsRequest
            {
                AppId = request.AppId,
                PermissionSet = request.PermissionSet,
                Drives = request.Drives,
            }, odinContext);
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