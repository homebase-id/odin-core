using System.Threading;
using System.Threading.Tasks;
using Odin.Services.Base;

namespace Odin.Services.Configuration.VersionUpgrade.Version3tov4
{
    /// <summary>
    /// v3 -&gt; v4: retired.  It gave the Confirmed and Auto Connections system circles Read on every anonymous
    /// drive.  Both circles are retired (#1809) and V19 -&gt; V20 deletes them, so there is nothing left to do;
    /// the version step stays so the ladder still counts through it.
    /// </summary>
    public class V3ToV4VersionMigrationService
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
