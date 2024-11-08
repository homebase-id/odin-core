﻿using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;
using Odin.Services.Membership.Connections.Verification;

namespace Odin.Services.Configuration.VersionUpgrade.Version1tov2
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V1ToV2VersionMigrationService(
        ILogger<V1ToV2VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleNetworkVerificationService verificationService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            //
            // Sync verification hash's across all connections
            //
            logger.LogDebug("Syncing verification hashes");
            await verificationService.SyncHashOnAllConnectedIdentities(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            logger.LogDebug("Validate verification has on all connections...");
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, 0, odinContext);
            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (identity.VerificationHash.IsNullOrEmpty())
                {
                    throw new OdinSystemException($"Verification hash missing for {identity.OdinId}");
                }
            }
            
            logger.LogDebug("Validate verification has on all connections - OK");
        }
    }
}