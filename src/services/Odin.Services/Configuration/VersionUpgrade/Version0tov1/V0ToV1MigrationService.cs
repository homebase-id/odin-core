﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;
using Odin.Core.Exceptions;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Apps;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.EncryptionKeyService;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

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
        TenantConfigService tenantConfigService,
        PublicPrivateKeyService publicPrivateKeyService,
        IdentityDatabase db)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            logger.LogDebug("Preparing Introductions Release for Identity [{identity}]", odinContext.Tenant);
            await PrepareIntroductionsReleaseAsync(odinContext, cancellationToken);

            await AutoFixCircleGrantsAsync(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            await ValidateIntroductionsReleaseAsync(odinContext, cancellationToken);
        }

        private async Task ValidateIntroductionsReleaseAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            //
            // Validate new ICR Key exists
            //
            logger.LogDebug("Validate new ICR Key exists...");
            var icrEccKey = await publicPrivateKeyService.GetEccFullKeyAsync(PublicPrivateKeyType.OnlineIcrEncryptedKey);
            if (icrEccKey == null)
            {
                throw new OdinSystemException("OnlineIcrEncryptedKey was not created");
            }

            logger.LogDebug("Validate new ICR Key exists - OK");

            cancellationToken.ThrowIfCancellationRequested();

            //
            // Validate system circles are correct
            //
            logger.LogDebug("Validate system circles are correct...");
            await AssertCircleDefinitionIsCorrect(SystemCircleConstants.ConfirmedConnectionsDefinition);
            await AssertCircleDefinitionIsCorrect(SystemCircleConstants.AutoConnectionsSystemCircleDefinition);
            cancellationToken.ThrowIfCancellationRequested();
            logger.LogDebug("Validate system circles are correct - OK");


            //
            //
            //
            logger.LogDebug("Validate new permission exists on all ICRs for ConfirmedConnectionsDefinition...");

            var invalidMembers = await circleNetworkService.GetInvalidMembersOfCircleDefinition(
                SystemCircleConstants.ConfirmedConnectionsDefinition, odinContext);

            if (invalidMembers.Any())
            {
                logger.LogError("Identities with invalid circle grant for confirmed connections circle : [{list}]",
                    string.Join(",", invalidMembers));

                throw new OdinSystemException("Invalid members found for confirmed connections circle");
            }

            logger.LogDebug("Validate new permission exists on all ICRs for ConfirmedConnectionsDefinition - OK");

            cancellationToken.ThrowIfCancellationRequested();

            //
            // Update the apps that use the new circle
            //
            logger.LogDebug("Verifying system apps have new circles and permissions...");
            await VerifyApp(SystemAppConstants.ChatAppRegistrationRequest, odinContext);
            await VerifyApp(SystemAppConstants.MailAppRegistrationRequest, odinContext);
            logger.LogDebug("Verifying system apps have new circles and permissions - OK");
            cancellationToken.ThrowIfCancellationRequested();
        }

        private async Task AssertCircleDefinitionIsCorrect(CircleDefinition expectedDefinition)
        {
            var existingDefinition = await circleDefinitionService.GetCircleAsync(expectedDefinition.Id);
            if (existingDefinition == null)
            {
                throw new OdinSystemException($"Definition does not exist with ID {expectedDefinition.Id}");
            }

            if (existingDefinition.Name != expectedDefinition.Name)
            {
                throw new OdinSystemException($"Name does not match expected definition with ID {expectedDefinition.Id}");
            }

            if (existingDefinition.Description != expectedDefinition.Description)
            {
                throw new OdinSystemException($"Description does not match expected definition for {expectedDefinition.Name}");
            }

            if (existingDefinition.Disabled != expectedDefinition.Disabled)
            {
                throw new OdinSystemException($"Disabled does not match expected definition for {expectedDefinition.Name}");
            }

            if (existingDefinition.Permissions != expectedDefinition.Permissions)
            {
                throw new OdinSystemException(
                    $"Circle Definition permission do not match expected definition for {expectedDefinition.Name}");
            }

            if (expectedDefinition.DriveGrants.Intersect(existingDefinition.DriveGrants).Count() != expectedDefinition.DriveGrants.Count())
            {
                throw new OdinSystemException(
                    $"Circle Definition DriveGrants do not match expected definition for {expectedDefinition.Name}");
            }
        }

        public async Task AutoFixCircleGrantsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            await using var tx = await db.BeginStackedTransactionAsync();

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
            
            tx.Commit();
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
            // Ensure all system drives exist (for older identities)
            //
            logger.LogDebug("Ensuring all system drives exist");
            await tenantConfigService.EnsureSystemDrivesExist(odinContext);
            cancellationToken.ThrowIfCancellationRequested();

            //
            // Ensure all system apps (for older identities)
            //
            logger.LogDebug("Ensuring all system apps exist");
            await tenantConfigService.EnsureBuiltInApps(odinContext);
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

        private async Task VerifyApp(AppRegistrationRequest request, IOdinContext odinContext)
        {
            var appReg = await appRegistrationService.GetAppRegistration(request.AppId, odinContext);

            if (appReg == null)
            {
                throw new OdinSystemException($"Failed to upgrade app {request.AppId} | {request.Name}. App not found");
            }

            if (appReg.AuthorizedCircles.Intersect(request.AuthorizedCircles).Count() != request.AuthorizedCircles.Count)
            {
                throw new OdinSystemException($"Failed to upgrade app {request.AppId} | {request.Name}. App not found");
            }

            if (appReg.CircleMemberPermissionSetGrantRequest.PermissionSet != request.CircleMemberPermissionGrant.PermissionSet)
            {
                throw new OdinSystemException($"Failed to upgrade app {request.AppId} | {request.Name}. " +
                                              $"CircleMemberPermissionGrant.PermissionSet does not match");
            }

            if (appReg.CircleMemberPermissionSetGrantRequest.Drives.Intersect(request.CircleMemberPermissionGrant.Drives).Count() !=
                request.CircleMemberPermissionGrant.Drives.Count())
            {
                throw new OdinSystemException($"Failed to upgrade app {request.AppId} | {request.Name}. " +
                                              $"CircleMemberPermissionGrant.Drives does not match");
            }

            if (appReg.Grant.PermissionSet != request.PermissionSet)
            {
                throw new OdinSystemException($"Failed to upgrade app {request.AppId} | {request.Name}. " +
                                              $"PermissionSet does not match");
            }

            if (appReg.Grant.DriveGrants.IntersectBy(request.Drives.Select(dg => dg.PermissionedDrive),
                    rdg => rdg.PermissionedDrive).Count() != request.Drives.Count())
            {
                throw new OdinSystemException($"Failed to upgrade app {request.AppId} | {request.Name}. " +
                                              $"Drives does not match");
            }
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