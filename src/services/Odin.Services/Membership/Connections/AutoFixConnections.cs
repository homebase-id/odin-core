﻿using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
        TenantSystemStorage tenantSystemStorage,
        CircleDefinitionService circleDefinitionService,
        CircleNetworkService circleNetworkService)
    {
        public async Task AutoFix(IOdinContext odinContext)
        {
            odinContext.Caller.AssertHasMasterKey();
            using var cn = tenantSystemStorage.CreateConnection();
            var allIdentities = await circleNetworkService.GetConnectedIdentities(int.MaxValue, 0, odinContext, cn);

            await cn.CreateCommitUnitOfWorkAsync(async () =>
            {
                foreach (var identity in allIdentities.Results)
                {
                    await FixIdentity(identity, odinContext, cn);
                }

                var allApps = await appRegistrationService.GetRegisteredApps(odinContext, cn);
                foreach (var app in allApps)
                {
                    logger.LogDebug("Calling ReconcileAuthorizedCircles for app {appName}", app.Name);
                    await circleNetworkService.ReconcileAuthorizedCircles(oldAppRegistration: null, app, odinContext, cn);
                }
            });
        }

        private async Task FixIdentity(IdentityConnectionRegistration icr, IOdinContext odinContext, DatabaseConnection cn)
        {
            foreach (var circleGrant in icr.AccessGrant.CircleGrants)
            {
                var circleId = circleGrant.Value.CircleId;
                
                var def = circleDefinitionService.GetCircle(circleId, cn);
                logger.LogDebug("Fixing Identity {odinId} in {circle}", icr.OdinId, def.Name);
                
                await circleNetworkService.RevokeCircleAccess(circleId, icr.OdinId, odinContext, cn);
                await circleNetworkService.GrantCircle(circleId, icr.OdinId, odinContext, cn);
            }
        }
    }
}