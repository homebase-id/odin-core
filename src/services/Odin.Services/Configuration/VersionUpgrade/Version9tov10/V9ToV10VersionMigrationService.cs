using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Base;

namespace Odin.Services.Configuration.VersionUpgrade.Version9tov10
{
    /// <summary>
    /// v9 → v10: retired.  It gave the Confirmed and Auto Connections system circles Read on the
    /// ProfileDrive and re-minted their members so they held the drive's storage key.  Both circles are
    /// retired (#1809) and V19 → V20 deletes them; the ProfileDrive key now comes from the Family, Friends and
    /// Work circles, which V19 → V20 grants.  The version step stays so the ladder still counts through it.
    /// </summary>
    public class V9ToV10VersionMigrationService
    {
        public Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
