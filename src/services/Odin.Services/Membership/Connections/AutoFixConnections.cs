using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Storage.Database.Identity;
using Odin.Core.Storage.SQLite;
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

            var allIdentities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, 0, odinContext);

            foreach (var identity in allIdentities.Results)
            {
                await FixIdentityAsync(identity, odinContext);
            }

            var allApps = await appRegistrationService.GetRegisteredAppsAsync(odinContext);
            foreach (var app in allApps)
            {
                logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext);
            }

            tx.Commit();
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