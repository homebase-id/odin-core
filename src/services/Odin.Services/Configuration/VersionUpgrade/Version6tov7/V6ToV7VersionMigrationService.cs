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
        IDriveManager driveManager)
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

            var momentsDrive = await driveManager.GetDriveAsync(SystemDriveConstants.MomentsDrive.Alias, false);
            if (momentsDrive == null)
            {
                throw new OdinSystemException("Moments drive not created");
            }
        }
    }
}
