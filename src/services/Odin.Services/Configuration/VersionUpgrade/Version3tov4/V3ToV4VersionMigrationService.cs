using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives.Management;
using Odin.Services.Mediator;
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
        CircleDefinitionService circleDefinitionService,
        DriveManager driveManager)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var anonymousDrives = await driveManager.GetAnonymousDrivesAsync(PageOptions.All, odinContext);

            foreach (var drive in anonymousDrives.Results)
            {
                await circleNetworkService.Handle(new DriveDefinitionAddedNotification
                {
                    OdinContext = odinContext,
                    IsNewDrive = false,
                    Drive = drive
                }, cancellationToken);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var confirmedCircle = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.ConfirmedConnectionsDefinition.Id);
            foreach (var dg in SystemCircleConstants.ConfirmedConnectionsDefinition.DriveGrants)
            {
                if (null == confirmedCircle.DriveGrants.FirstOrDefault(cdg => cdg.PermissionedDrive == dg.PermissionedDrive))
                {
                    logger.LogError("Failed {cn} is missing drive grant {dg}", confirmedCircle.Name, dg.PermissionedDrive);
                    throw new OdinSystemException($"Validation failed.  Confirmed circle missing drive grant {dg.PermissionedDrive}");
                }

                logger.LogDebug("Validated {cn} has drive grant {dg}", confirmedCircle.Name, dg.PermissionedDrive);
            }

            var autoCircle = await circleDefinitionService.GetCircleAsync(SystemCircleConstants.AutoConnectionsSystemCircleDefinition.Id);
            foreach (var dg in SystemCircleConstants.AutoConnectionsSystemCircleDefinition.DriveGrants)
            {
                if (null == autoCircle.DriveGrants.FirstOrDefault(cdg => cdg.PermissionedDrive == dg.PermissionedDrive))
                {
                    logger.LogError("Failed {cn} is missing drive grant {dg}", autoCircle.Name, dg.PermissionedDrive);
                    throw new OdinSystemException($"Validation failed.  Auto-Connect circle missing drive grant {dg.PermissionedDrive}");
                }

                logger.LogDebug("Validated {cn} has drive grant {dg}", autoCircle.Name, dg.PermissionedDrive);
            }
        }
    }
}