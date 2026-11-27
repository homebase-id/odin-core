using System.Linq;
using Odin.Services.Apps;
using Odin.Services.Apps.Builtin;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.Configuration.VersionUpgrade.Version6tov7
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V6ToV7VersionMigrationService(
        ILogger<V6ToV7VersionMigrationService> logger,
        TenantConfigService tenantConfigService,
        IDriveManager driveManager,
        UninstalledBuiltinApps uninstalledApps)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            logger.LogDebug("Ensuring system drives exist on identity: [{identity}]", odinContext.Tenant);
            await tenantConfigService.EnsureSystemDrivesExist(odinContext);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Not made when the owner uninstalled Moments (#2138).
            var momentsDrive = await driveManager.GetDriveAsync(WellKnownAppDrives.MomentsDrive.Alias, false);
            if (momentsDrive == null && !(await uninstalledApps.GetAsync()).Contains(SystemAppConstants.MomentsAppId))
            {
                throw new OdinSystemException("Moments drive not created");
            }
        }
    }
}
