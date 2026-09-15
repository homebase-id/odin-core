using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Apps.Builtin;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;
using Odin.Services.Security.PasswordRecovery.Shamir;

namespace Odin.Services.Configuration.VersionUpgrade.Version17tov18
{
    /// <summary>
    /// v17 -&gt; v18: moves the existing population into the per-app built-in circles that replace the two
    /// system circles.
    /// </summary>
    /// <remarks>
    /// Confirmed Connections and Auto Connections are frozen platform bundles that grant across six
    /// drives and belong to no app.  Apps now own their circles, so the capability those bundles carried
    /// is being re-expressed as <c>BuiltinCircles.ChatCircle</c>, <c>MomentsCircle</c> and
    /// <c>RecoveryCircle</c> -- each granting only drives its own app owns.  Provisioning creates those
    /// circles but never populates them, so on every identity that predates this they are empty, and the
    /// capability still hangs entirely off the two bundles.  This is the pass that moves the people.
    ///
    /// <para>
    /// Three passes, because the three circles qualify people differently and each has to be derived from
    /// what the identity already records rather than from membership of the circles being retired:
    /// </para>
    /// <list type="bullet">
    /// <item><b>Moments</b> is <see cref="CircleGrantOn.Review"/>, so its population is everyone the owner
    /// has vetted -- <c>ReviewedAt != null</c>.  v15 -&gt; v16 backfilled that column from Confirmed
    /// membership, so "was confirmed" and "was reviewed" are already the same set plus anyone reviewed
    /// since.  Reading the column rather than the circle means the answer keeps working after the circle
    /// is gone, and picks up reviews the backfill could not have seen.</item>
    /// <item><b>Chat</b> is <see cref="CircleGrantOn.Connect"/>: being connected at all is the qualifying
    /// act, which every connection has already performed.  So the population is every connected identity,
    /// reviewed or not -- an auto-connection could chat before this and must still be able to after.</item>
    /// <item><b>Recovery</b> is the one that deliberately narrows.  Confirmed Connections granted write on
    /// the shard drive to every confirmed contact; the replacement grants it only to the people actually
    /// holding a shard, taken from the dealer's own shard package.  Carrying the old breadth over would
    /// keep handing a write grant to contacts who hold nothing, which is the point of the narrowing.</item>
    /// </list>
    ///
    /// <para>
    /// Nothing is removed.  The two system circles keep every member and every grant they have; retiring
    /// them is a separate act, and doing half of it here would take capability away on the strength of a
    /// backfill nobody has watched run yet.  For the same reason each pass only ever adds: a member it
    /// finds already in place is a skip, so a partial run can simply be repeated and a second run is a
    /// no-op.
    /// </para>
    ///
    /// <para>
    /// A single contact in a bad state does not stop a pass -- it is logged and the pass moves on, the way
    /// <see cref="CircleNetworkService.ProcessPendingEnrollmentsForAppAsync"/> does.  One connection whose
    /// key store cannot be read is not the rest of the address book's problem.  Validation then re-derives
    /// all three populations from storage and throws if anyone who should have landed did not, so a pass
    /// that quietly dropped people fails the upgrade rather than reporting success.
    /// </para>
    /// </remarks>
    public class V17ToV18VersionMigrationService(
        ILogger<V17ToV18VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleDefinitionService circleDefinitionService,
        ShamirConfigurationService shamirConfigurationService)
    {
        /// <summary>
        /// All three passes, in order.  The ladder runs them as three separate phases so a failure names
        /// the pass it happened in; this is the same work for a caller that wants it as one call.
        /// </summary>
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            await EnrollReviewedContactsInMomentsAsync(odinContext, cancellationToken);
            await EnrollConnectedContactsInChatAsync(odinContext, cancellationToken);
            await EnrollShardHoldersInRecoveryAsync(odinContext, cancellationToken);
        }

        /// <summary>
        /// Pass A: everyone the owner has reviewed joins the Moments circle.
        /// </summary>
        public async Task<int> EnrollReviewedContactsInMomentsAsync(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var circleId = BuiltinCircles.MomentsCircle.Id;
            if (!await CircleExistsAsync(circleId, BuiltinCircles.MomentsCircle.Name))
            {
                return 0;
            }

            var enrolled = 0;
            foreach (var icr in await GetConnectedAsync(odinContext))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (icr.ReviewedAt == null || HoldsCircle(icr, circleId))
                {
                    continue;
                }

                try
                {
                    if (await circleNetworkService.ApplyReviewedCircleAsync(circleId, icr.OdinId, odinContext))
                    {
                        enrolled++;
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "v17->v18: could not add {odinId} to the Moments circle", icr.OdinId);
                }
            }

            logger.LogInformation("v17->v18: added {count} reviewed contact(s) to the Moments circle", enrolled);
            return enrolled;
        }

        /// <summary>
        /// Pass B: every connected identity joins the Chat circle, reviewed or not.
        /// </summary>
        /// <remarks>
        /// Goes through <see cref="CircleNetworkService.ApplyAmbientCircleAsync"/> rather than
        /// <c>GrantCircleAsync</c>, which refuses anyone still holding the Auto Connections circle --
        /// most of the very population this pass exists to move.
        /// </remarks>
        public async Task<int> EnrollConnectedContactsInChatAsync(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var circleId = BuiltinCircles.ChatCircle.Id;
            if (!await CircleExistsAsync(circleId, BuiltinCircles.ChatCircle.Name))
            {
                return 0;
            }

            var enrolled = 0;
            foreach (var icr in await GetConnectedAsync(odinContext))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (HoldsCircle(icr, circleId))
                {
                    continue;
                }

                try
                {
                    await circleNetworkService.ApplyAmbientCircleAsync(circleId, icr.OdinId, odinContext);
                    enrolled++;
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "v17->v18: could not add {odinId} to the Chat circle", icr.OdinId);
                }
            }

            logger.LogInformation("v17->v18: added {count} connected contact(s) to the Chat circle", enrolled);
            return enrolled;
        }

        /// <summary>
        /// Pass C: the people actually holding a shard of the recovery key join the Recovery circle.
        /// </summary>
        /// <remarks>
        /// The roster comes from the dealer's own shard package -- the single file the dealer keeps on the
        /// shard recovery drive, read here through <see cref="ShamirConfigurationService.GetRedactedConfig"/>.
        /// <c>ConfigureShards</c> rewrites that package whole every time the owner reconfigures, so its
        /// envelopes are who holds a shard now rather than who ever held one.  That is the only record of
        /// current holders there is; Confirmed membership is not one, which is exactly why the old broad
        /// grant is being narrowed.
        /// <para>
        /// An identity that has never configured recovery has no package, and this does nothing.
        /// </para>
        /// <para>
        /// A holder who is not connected, or who carries no review, is skipped and named in the log rather
        /// than enrolled.  Recovery is a <see cref="CircleGrantOn.Review"/> circle and membership of one
        /// must imply a review; the alternative -- stamping a review to make this pass's own work possible
        /// -- would be recording that the owner vetted someone on no evidence but this code wanting it.
        /// The set should be empty in practice, since only the Confirmed circle ever granted write on the
        /// shard drive and v15 -&gt; v16 turned exactly that membership into a review, so a warning here is
        /// worth seeing.
        /// </para>
        /// </remarks>
        public async Task<int> EnrollShardHoldersInRecoveryAsync(IOdinContext odinContext,
            CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            var circleId = BuiltinCircles.RecoveryCircle.Id;
            if (!await CircleExistsAsync(circleId, BuiltinCircles.RecoveryCircle.Name))
            {
                return 0;
            }

            var holders = await GetShardHoldersAsync(odinContext);
            if (holders.Count == 0)
            {
                logger.LogDebug("v17->v18: no shard configuration on this identity; the Recovery circle is left empty");
                return 0;
            }

            var enrolled = 0;
            foreach (var odinId in holders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (await circleNetworkService.ApplyReviewedCircleAsync(circleId, odinId, odinContext))
                    {
                        enrolled++;
                    }
                    else
                    {
                        logger.LogDebug(
                            "v17->v18: shard holder {odinId} was not added to the Recovery circle; they are " +
                            "already a member, not connected, or carry no review", odinId);
                    }
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "v17->v18: could not add shard holder {odinId} to the Recovery circle", odinId);
                }
            }

            logger.LogInformation("v17->v18: added {count} of {total} shard holder(s) to the Recovery circle",
                enrolled, holders.Count);
            return enrolled;
        }

        /// <summary>
        /// Everyone each pass should have placed is in the circle it should have placed them in.
        /// </summary>
        /// <remarks>
        /// Re-derived from storage rather than compared against what the passes believed they wrote, so a
        /// contact that was logged and skipped is caught here rather than reported as a success.  A circle
        /// this identity does not have is not checked: there is nothing it could have landed in.
        /// </remarks>
        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var connected = await GetConnectedAsync(odinContext);

            await AssertAllHoldAsync(BuiltinCircles.MomentsCircle,
                connected.Where(i => i.ReviewedAt != null).ToList(), cancellationToken);

            await AssertAllHoldAsync(BuiltinCircles.ChatCircle, connected, cancellationToken);

            // Only holders that could have been enrolled: the pass deliberately leaves an unreviewed or
            // disconnected holder out, and validation must not demand what the pass refuses to do.
            var holders = await GetShardHoldersAsync(odinContext);
            var eligibleHolders = connected
                .Where(i => i.ReviewedAt != null && holders.Any(h => h == i.OdinId))
                .ToList();

            await AssertAllHoldAsync(BuiltinCircles.RecoveryCircle, eligibleHolders, cancellationToken);
        }

        private async Task AssertAllHoldAsync(CircleDefinition definition,
            IReadOnlyCollection<IdentityConnectionRegistration> expected, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await circleDefinitionService.GetCircleAsync(definition.Id) == null)
            {
                return;
            }

            var missing = expected.Where(i => !HoldsCircle(i, definition.Id)).Select(i => i.OdinId.DomainName).ToList();
            if (missing.Count != 0)
            {
                throw new OdinSystemException(
                    $"v17->v18 left {missing.Count} identity(s) out of the {definition.Name} circle: " +
                    string.Join(", ", missing));
            }
        }

        /// <summary>
        /// Whether the circle this pass wants to fill is even on this identity.
        /// </summary>
        /// <remarks>
        /// Provisioning creates all three (v13 -&gt; v14 runs <c>BuiltinProvisioner.EnsureAllAsync</c>, and
        /// the ladder ensures drives up front before any migration runs), so an absent one means the owner
        /// deleted it.  That is their choice to have made and re-creating it here would undo it, so the
        /// pass says so and does nothing instead.
        /// </remarks>
        private async Task<bool> CircleExistsAsync(Guid circleId, string name)
        {
            if (await circleDefinitionService.GetCircleAsync(circleId) != null)
            {
                return true;
            }

            logger.LogWarning("v17->v18: the {circle} circle does not exist on this identity; nobody was added to it",
                name);
            return false;
        }

        private static bool HoldsCircle(IdentityConnectionRegistration icr, Guid circleId)
        {
            return icr.PeerKeyStore?.CircleGrants.ContainsKey(circleId) ?? false;
        }

        /// <summary>
        /// Every connected identity, read out in full before anything is written.
        /// </summary>
        /// <remarks>
        /// Materialised rather than paged through while enrolling: the passes rewrite the very rows the
        /// cursor is walking, and a page drawn after a write is a page of records the loop has already
        /// changed.  The list is one row per connection, so holding it is cheap next to the risk.
        /// </remarks>
        private async Task<List<IdentityConnectionRegistration>> GetConnectedAsync(IOdinContext odinContext)
        {
            var all = new List<IdentityConnectionRegistration>();

            string cursor = null;
            do
            {
                var page = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, cursor, odinContext);
                cursor = page.Cursor;
                all.AddRange(page.Results);
            } while (!string.IsNullOrEmpty(cursor));

            return all;
        }

        private async Task<List<OdinId>> GetShardHoldersAsync(IOdinContext odinContext)
        {
            var config = await shamirConfigurationService.GetRedactedConfig(odinContext);

            return (config?.Envelopes ?? [])
                .Select(e => e.Player.OdinId)
                .Distinct()
                .ToList();
        }
    }
}
