using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Services.Base;

namespace Odin.Services.Configuration.VersionUpgrade.Version5tov6
{
    /// <summary>
    /// Service to handle converting data between releases
    /// </summary>
    public class V5ToV6VersionMigrationService(
        ILogger<V5ToV6VersionMigrationService> logger,
        TenantConfigService tenantConfigService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            logger.LogDebug("Preparing Password recovery Release for Identity [{identity}]", odinContext.Tenant);
            await PreparePasswordRecoveryReleaseAsync(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            await ValidateIntroductionsReleaseAsync(odinContext, cancellationToken);
        }

        /// <summary>
        /// Handles the changes to production data required for the introductions feature
        /// </summary>
        private async Task PreparePasswordRecoveryReleaseAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            //
            // Ensure all system drives exist (for older identities)
            //
            logger.LogDebug("Ensuring all system drives exist");
            await tenantConfigService.EnsureSystemDrivesExist(odinContext);
            cancellationToken.ThrowIfCancellationRequested();
        }
        
        private async Task ValidateIntroductionsReleaseAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            await Task.CompletedTask;
            
            //TODO verify system drive was created
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}