using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version15tov16
{
    /// <summary>
    /// v15 -&gt; v16: backfills <c>Connections.ReviewedAt</c> for connections the owner had already vetted.
    /// </summary>
    /// <remarks>
    /// Membership of the Confirmed Connections system circle is what "vetted" meant before this column
    /// existed -- it was only ever reached through the owner's explicit confirm -- so those connections are
    /// exactly the ones that would have carried a review stamp had there been one to carry
    /// (docs/drive-addressing.md: "The vetted backfill becomes a plain UPDATE").
    ///
    /// <para>
    /// The stamp is the migration's own run time, not the connection's creation or modification time.  The
    /// column records when the owner reviewed, and that moment was never recorded -- inventing a plausible
    /// one from <c>created</c> would be a fabricated audit trail, while the run time is at least honestly
    /// "no later than this".
    /// </para>
    ///
    /// <para>
    /// Additive and repeatable: <see cref="CircleNetworkService.StampReviewedIfUnsetAsync"/> is set-once, so
    /// re-running this never moves a stamp.  Everything else stays NULL -- an auto-connection nobody ever
    /// looked at is New, which is the whole point of the column.
    /// </para>
    /// </remarks>
    public class V15ToV16VersionMigrationService(
        ILogger<V15ToV16VersionMigrationService> logger,
        CircleNetworkService circleNetworkService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var confirmed = await GetConfirmedConnectionsAsync(odinContext);

            foreach (var odinId in confirmed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await circleNetworkService.StampReviewedIfUnsetAsync(odinId);
            }

            logger.LogInformation("v15->v16: stamped ReviewedAt on {count} previously-confirmed connection(s)",
                confirmed.Count);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var confirmed = await GetConfirmedConnectionsAsync(odinContext);

            foreach (var odinId in confirmed)
            {
                var icr = await circleNetworkService.GetIcrAsync(odinId, odinContext);

                // A member that is no longer connected has no registration to stamp; nothing to validate.
                if (icr is { ReviewedAt: null } && icr.IsConnected())
                {
                    throw new OdinSystemException(
                        $"v15->v16: {odinId} is a member of the Confirmed Connections circle but still has no ReviewedAt");
                }
            }
        }

        private async Task<List<OdinId>> GetConfirmedConnectionsAsync(IOdinContext odinContext)
        {
            var members = await circleNetworkService.GetCircleMembersAsync(
                SystemCircleConstants.ConfirmedConnectionsCircleId, odinContext);

            return members.ToList();
        }
    }
}
