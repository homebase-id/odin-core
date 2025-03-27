using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version3tov4
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V3ToV4VersionMigrationService(
        ILogger<V3ToV4VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleDefinitionService circleDefinitionService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            
            cancellationToken.ThrowIfCancellationRequested();
            
            //
            // Update the system circles to see new drive grants
            //
            logger.LogDebug("Creating new circles; renaming existing ones");
            await circleDefinitionService.EnsureSystemCirclesExistAsync();

            //
            // This will reapply the grants
            //
            logger.LogDebug("Reapplying permissions for ConfirmedConnections Circle");
            await circleNetworkService.UpdateCircleDefinitionAsync(SystemCircleConstants.ConfirmedConnectionsDefinition, odinContext);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var confirmedCircle = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsDefinition.Id);
            foreach (var dg in SystemCircleConstants.ConfirmedConnectionsDefinition.DriveGrants)
            {
                if (null == confirmedCircle.DriveGrants.FirstOrDefault(cdg => cdg.PermissionedDrive == dg.PermissionedDrive))
                {
                    throw new OdinSystemException($"Validation failed.  Confirmed circle missing drive grant {dg.PermissionedDrive}");
                }
            }
            
            var autoCircle = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.AutoConnectionsSystemCircleDefinition.Id);
            foreach (var dg in SystemCircleConstants.AutoConnectionsSystemCircleDefinition.DriveGrants)
            {
                if (null == autoCircle.DriveGrants.FirstOrDefault(cdg => cdg.PermissionedDrive == dg.PermissionedDrive))
                {
                    throw new OdinSystemException($"Validation failed.  Auto-Connect circle missing drive grant {dg.PermissionedDrive}");
                }
            }
        }
    }
}