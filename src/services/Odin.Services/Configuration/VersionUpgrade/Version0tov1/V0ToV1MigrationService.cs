using System;
using System.Threading;
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
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            logger.LogDebug("Preparing Introductions Release for Identity [{identity}]", odinContext.Tenant);
            await PrepareIntroductionsReleaseAsync(odinContext, cancellationToken);

            await AutoFixCircleGrantsAsync(odinContext,cancellationToken);
        }

        public async Task AutoFixCircleGrantsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, 0, odinContext);

            //TODO CONNECTIONS
            // await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                foreach (var identity in allIdentities.Results)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    await FixIdentityAsync(identity, odinContext);
                }

                var allApps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
                foreach (var app in allApps)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                    await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext);
                }
            }
            //);
        }

        /// <summary>
        /// Handles the changes to production data required for the introductions feature
        /// </summary>
        private async Task PrepareIntroductionsReleaseAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            //
            // Clean up old circle (from development)
            //
            await DeleteOldCirclesAsync(odinContext);

            cancellationToken.ThrowIfCancellationRequested();

            //
            // Generate new Online Icr Encrypted ECC Key
            //
            logger.LogDebug("Creating new Online Icr Encrypted ECC Key");
            await publicPrivateKeyService.CreateInitialKeysAsync(odinContext);

            cancellationToken.ThrowIfCancellationRequested();

            //
            // Create new circles, rename existing ones
            //
            logger.LogDebug("Creating new circles; renaming existing ones");
            await circleDefinitionService.EnsureSystemCirclesExistAsync();

            cancellationToken.ThrowIfCancellationRequested();
            
            //
            // This will reapply the grants since we added a new permission
            //
            logger.LogDebug("Reapplying permissions for ConfirmedConnections Circle");
            await circleNetworkService.UpdateCircleDefinitionAsync(SystemCircleConstants.ConfirmedConnectionsDefinition, odinContext);

            cancellationToken.ThrowIfCancellationRequested();
            
            //
            // Update the apps that use the new circle
            //
            logger.LogDebug("Updating system apps with new circles and permissions");
            await UpdateApp(SystemAppConstants.ChatAppRegistrationRequest, odinContext);
            await UpdateApp(SystemAppConstants.MailAppRegistrationRequest, odinContext);
            cancellationToken.ThrowIfCancellationRequested();

            //
            // Sync verification hash's across all connections
            //
            logger.LogInformation("Syncing verification hashes");
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, 0, odinContext);

            //TODO CONNECTIONS
            // await db.CreateCommitUnitOfWorkAsync(async () =>
            {
                foreach (var identity in allIdentities.Results)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    //
                    // Sync verification hash
                    //
                    if (identity.VerificationHash?.Length == 0)
                    {
                        var success = await verificationService.SynchronizeVerificationHashAsync(identity.OdinId, odinContext);
                        logger.LogDebug("EnsureVerificationHash for {odinId}.  Succeeded: {success}", identity.OdinId, success);
                    }
                }
            }
            //);
        }

        private async Task DeleteOldCirclesAsync(IOdinContext odinContext)
        {
            try
            {
                Guid confirmedCircleGuid = Guid.Parse("ba4f80d2eac44b31afc1a3dfe7043411");
                var definition = await circleDefinitionService.GetCircleAsync(confirmedCircleGuid);
                if (definition == null)
                {
                    return;
                }

                logger.LogDebug("Deleting obsolete circle {name}", definition.Name);

                var members = await circleNetworkService.GetCircleMembersAsync(confirmedCircleGuid, odinContext);

                foreach (var member in members)
                {
                    await circleNetworkService.RevokeCircleAccessAsync(confirmedCircleGuid, member, odinContext);
                }

                await circleNetworkService.DeleteCircleDefinitionAsync(confirmedCircleGuid, odinContext);
            }
            catch (Exception e)
            {
                logger.LogDebug(e, "Error while deleting obsolete circles");
            }
        }

        private async Task UpdateApp(AppRegistrationRequest request, IOdinContext odinContext)
        {
            await appRegistrationService.UpdateAuthorizedCirclesAsync(new UpdateAuthorizedCirclesRequest
            {
                AppId = request.AppId,
                AuthorizedCircles = request.AuthorizedCircles,
                CircleMemberPermissionGrant = request.CircleMemberPermissionGrant
            }, odinContext);

            await appRegistrationService.UpdateAppPermissionsAsync(new UpdateAppPermissionsRequest
            {
                AppId = request.AppId,
                PermissionSet = request.PermissionSet,
                Drives = request.Drives,
            }, odinContext);
        }

        private async Task FixIdentityAsync(IdentityConnectionRegistration icr, IOdinContext odinContext)
        {
            foreach (var circleGrant in icr.AccessGrant.CircleGrants)
            {
                var circleId = circleGrant.Value.CircleId;

                var def = await circleDefinitionService.GetCircleAsync(circleId);
                logger.LogDebug("Fixing Identity {odinId} in {circle}", icr.OdinId, def.Name);

                await circleNetworkService.RevokeCircleAccessAsync(circleId, icr.OdinId, odinContext);
                await circleNetworkService.GrantCircleAsync(circleId, icr.OdinId, odinContext);
            }
        }
    }
}