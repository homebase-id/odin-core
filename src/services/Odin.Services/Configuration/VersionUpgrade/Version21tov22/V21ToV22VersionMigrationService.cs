using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Apps.Builtin;

namespace Odin.Services.Configuration.VersionUpgrade.Version21tov22
{
    /// <summary>
    /// v21 → v22: the Feed circle gets Read, with the storage key, on every channel drive the Feed app owns, so a custom
    /// or collaborative channel's encrypted posts reach its members again (#1956, <see cref="FeedCircleChannelReadGrants"/>).
    /// </summary>
    /// <remarks>
    /// A channel drive made from now on is granted when it is created; this covers the ones made before that the Feed
    /// circle has no grant on.  The grant is added to the circle as the owner has it, not reset to the built-in
    /// definition, and every member is re-minted.
    /// </remarks>
    public class V21ToV22VersionMigrationService(FeedCircleChannelReadGrants channelReadGrants)
    {
        public async Task GrantFeedCircleReadOnChannelDrivesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            await channelReadGrants.GrantReadOnAllChannelDrivesAsync(odinContext, cancellationToken);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var missing = await channelReadGrants.UngrantedChannelDrivesAsync(odinContext);
            if (missing.Count > 0)
            {
                throw new OdinSystemException(
                    $"The Feed circle has no grant on channel drive(s) {string.Join(", ", missing.Select(d => d.Alias))}");
            }
        }
    }
}
