using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version16tov17
{
    /// <summary>
    /// v16 → v17: hands the identity's relationship circles -- Friends, Family, Work, Acquaintances --
    /// to the chat app, so the app that presents them also owns them; and backfills the Reviewed
    /// Connections circle, which did not exist when these identities were set up.
    ///
    /// <para>
    /// These used to exist only because the owner console's setup wizard created them, through
    /// <see cref="InitialSetupRequest.Circles"/>, which means they arrived as owner circles
    /// (<c>AppId == null</c>) on every identity that ran it. A new identity now gets them chat-owned from
    /// <c>TenantConfigService.RegisterChatAppAsync</c>; this is the same set on the identities that
    /// predate that, and the two agree because both use <see cref="SystemAppConstants.ChatRelationshipCircles"/>.
    /// </para>
    ///
    /// <para>
    /// Nothing else can move them: <see cref="CircleDefinitionService.UpdateAsync"/> refuses to reassign
    /// <c>AppId</c> at all, and <see cref="CircleDefinitionService.CreateOrUpdateAppCircleAsync"/> refuses
    /// to take over a circle the app does not already own. Hence a migration, using
    /// <see cref="CircleDefinitionService.SetOwningAppAsync"/>.
    /// </para>
    ///
    /// <para>
    /// Matched by id, which is <c>md5(name)</c> on both sides, so the circles are found whatever the owner
    /// has since renamed them to. An identity that never ran the wizard simply has nothing to move: the
    /// migration rebinds what is there and never creates a circle that was not.
    /// </para>
    ///
    /// <para>
    /// Ownership only. Membership, grants, name, description and <see cref="CircleGrantOn"/> are left
    /// untouched -- these are the owner's circles with the owner's people in them, and the migration is
    /// about which app manages them from here, not about what they grant. It runs whether or not chat is
    /// installed: an identity that installs chat later would otherwise hit
    /// <see cref="OdinClientErrorCode.CircleNotOwnedByApp"/> the first time the app tried to manage one.
    /// </para>
    /// </summary>
    public class V16ToV17VersionMigrationService(
        ILogger<V16ToV17VersionMigrationService> logger,
        CircleDefinitionService circleDefinitionService,
        CircleMembershipService circleMembershipService,
        CircleNetworkService circleNetworkService)
    {
        public async Task UpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            await BackfillReviewedCircleAsync(odinContext, cancellationToken);

            foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var circle = await circleDefinitionService.GetCircleAsync(declared.Id);

                if (circle == null)
                {
                    logger.LogDebug("v16->v17: no '{circle}' circle ({id}) on this identity; skipping",
                        declared.Name, declared.Id);
                    continue;
                }

                if (circle.AppId.HasValue && circle.AppId != SystemAppConstants.ChatAppId)
                {
                    // Some other app already owns it. Taking it away would break that app's ability to
                    // manage its own circle, which is not this migration's call to make.
                    logger.LogWarning("v16->v17: '{circle}' ({id}) is owned by app {appId}; leaving it alone",
                        circle.Name, declared.Id, circle.AppId);
                    continue;
                }

                await circleDefinitionService.SetOwningAppAsync(declared.Id, SystemAppConstants.ChatAppId);

                logger.LogInformation("v16->v17: '{circle}' ({id}) is now owned by the chat app",
                    circle.Name, declared.Id);
            }
        }

        /// <summary>
        /// Creates the Reviewed Connections circle and puts every already-reviewed contact in it.
        /// </summary>
        /// <remarks>
        /// From here on the two happen together -- the code that stamps <c>ReviewedAt</c> adds them to the
        /// circle -- but every connection reviewed before this upgrade was stamped when no such circle
        /// existed. Without the backfill those contacts read as reviewed and cannot write their recovery
        /// shards, which is the one grant the circle carries.
        /// <para>
        /// Membership only where the stamp already says so: an unreviewed contact is left alone, and this
        /// never stamps anyone. Re-running is safe -- a contact already in the circle is skipped.
        /// </para>
        /// </remarks>
        private async Task BackfillReviewedCircleAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            // The definition first: these identities predate it, so EnsureSystemCirclesExistAsync has never
            // run with it in the constant.
            await circleMembershipService.CreateSystemCirclesAsync(odinContext);

            var circleId = SystemCircleConstants.ReviewedConnectionsCircleId;
            var identities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);

            var added = 0;
            foreach (var icr in identities.Results)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!icr.IsReviewed())
                {
                    continue;
                }

                if (icr.PeerKeyStore?.CircleGrants?.ContainsKey(circleId.Value) ?? false)
                {
                    continue;
                }

                await circleNetworkService.GrantCircleAsync(circleId, icr.OdinId, odinContext);
                added++;
            }

            logger.LogInformation("v16->v17: added {count} reviewed contact(s) to the Reviewed Connections circle", added);
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await circleDefinitionService.GetCircleAsync(SystemCircleConstants.ReviewedConnectionsCircleId) == null)
            {
                throw new OdinSystemException("Validation failed: the Reviewed Connections circle was not created");
            }

            var identities = await circleNetworkService.GetConnectedIdentitiesAsync(int.MaxValue, null, odinContext);
            foreach (var icr in identities.Results.Where(i => i.IsReviewed()))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!(icr.PeerKeyStore?.CircleGrants?.ContainsKey(SystemCircleConstants.ReviewedConnectionsCircleId.Value) ?? false))
                {
                    throw new OdinSystemException(
                        $"Validation failed: reviewed contact {icr.OdinId} is not in the Reviewed Connections circle");
                }
            }

            foreach (var declared in SystemAppConstants.ChatRelationshipCircles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var circle = await circleDefinitionService.GetCircleAsync(declared.Id);

                if (circle == null)
                {
                    continue;
                }

                // A circle another app already owned was deliberately skipped by the upgrade; still an
                // owner circle is the one outcome that means the upgrade did not do its job.
                if (circle.AppId == null)
                {
                    throw new OdinSystemException(
                        $"Validation failed: circle '{declared.Name}' ({declared.Id}) is still an owner circle");
                }
            }
        }
    }
}
