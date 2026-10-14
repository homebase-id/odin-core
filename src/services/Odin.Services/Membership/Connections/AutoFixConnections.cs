using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;

namespace Odin.Services.Membership.Connections
{
    /// <summary>
    /// Temporary service to fix circle grants and app circle grants
    /// </summary>
    public class ConnectionAutoFixService(
        ILogger<ConnectionAutoFixService> logger,
        IAppRegistrationService appRegistrationService,
        CircleDefinitionService circleDefinitionService,
        CircleNetworkService circleNetworkService,
        IdentityDatabase db)
    {
        public async Task AutoFixAsync(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();

            await using var tx = await db.BeginStackedTransactionAsync();

            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            // Re-mint every circle any connection holds, once per circle: UpdateCircleDefinitionAsync
            // re-creates the grant, storage keys included, for each connected member.  This used to
            // revoke and re-grant per connection through GrantCircleAsync, which is retired -- and which
            // refused anyone in the Auto Connections circle, so auto-connected contacts were skipped.
            // A grant for a circle whose definition is gone is left alone.
            var circleIds = allIdentities.Results
                .SelectMany(identity => identity.PeerKeyStore?.CircleGrants.Keys ?? Enumerable.Empty<Guid>())
                .Distinct()
                .ToList();

            foreach (var circleId in circleIds)
            {
                var definition = await circleDefinitionService.GetCircleAsync(circleId);
                if (definition == null)
                {
                    logger.LogDebug("Circle {circleId} has no definition; leaving its grants alone", circleId);
                    continue;
                }

                logger.LogDebug("Re-minting circle {circle} for its members", definition.Name);
                await circleNetworkService.UpdateCircleDefinitionAsync(definition, odinContext);
            }

            var allApps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in allApps)
            {
                logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext);
            }

            tx.Commit();
        }
    }
}