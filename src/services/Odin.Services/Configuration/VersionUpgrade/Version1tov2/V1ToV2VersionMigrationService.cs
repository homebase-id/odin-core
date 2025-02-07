using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
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
            var invalidIdentities = new List<OdinId>();
            logger.LogDebug("Validate verification has on all connections...");
            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, 0, odinContext);
            foreach (var identity in allIdentities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (identity.VerificationHash.IsNullOrEmpty())
                {
                    invalidIdentities.Add(identity.OdinId);
                    // throw new OdinSystemException($"Verification hash missing for {identity.OdinId}");
                }
            }

            if (invalidIdentities.Count > 0)
            {
                // Option - if there are any identities that are not upgraded, this could be enqueued to run again
                // maybe use a job?
                logger.LogDebug("Validating verification hash-sync.  Failed on the following identities:[{list}]",
                    string.Join(",", invalidIdentities));
                // throw new OdinSystemException($"Validating verification failed for {invalidIdentities.Count} identities");
            }
            else
            {
                logger.LogDebug("Validate verification has on all connections - OK");
            }
        }
    }
}