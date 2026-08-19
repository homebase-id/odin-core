using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Time;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version12tov13
{
    /// <summary>
    /// v12 → v13: backfills <see cref="IdentityConnectionRegistration.ReviewedAt"/> for connections the
    /// owner already confirmed.
    ///
    /// <para>
    /// The review stamp is a new column on the Connections table, and it is what the redacted
    /// <c>Vetted</c> flag is now computed from. Before this migration <c>Vetted</c> meant "connected and a
    /// member of the Confirmed Connections system circle"; after it, it means <c>ReviewedAt != null</c>.
    /// Without the backfill every already-confirmed contact would silently read as unreviewed — the
    /// contact book would show the owner's whole address book as New.
    /// </para>
    ///
    /// <para>
    /// The stamp is set to the connection's last-modified time rather than "now", so the backfill does not
    /// claim the owner reviewed everyone the moment they upgraded. It is an approximation of a fact we
    /// never recorded; the alternative — leaving it null — loses the fact entirely.
    /// </para>
    /// </summary>
    public class V12ToV13VersionMigrationService(
        ILogger<V12ToV13VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleNetworkStorage circleNetworkStorage)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var stamped = 0;

            var identities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);
            foreach (var icr in identities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (icr.IsReviewed())
                {
                    continue;
                }

                if (!icr.IsConfirmedConnection())
                {
                    continue;
                }

                await circleNetworkStorage.UpdateReviewedAtAsync(icr.OdinId, icr.Status, icr.LastUpdated);
                stamped++;

                logger.LogDebug("Backfilled ReviewedAt for confirmed connection [{identity}]", icr.OdinId);
            }

            logger.LogInformation("v12->v13: stamped ReviewedAt on {count} previously-confirmed connection(s)", stamped);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var identities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);
            foreach (var icr in identities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (icr.IsConfirmedConnection() && !icr.IsReviewed())
                {
                    throw new OdinSystemException(
                        $"Validation failed: confirmed connection [{icr.OdinId}] has no ReviewedAt stamp");
                }
            }
        }
    }
}
