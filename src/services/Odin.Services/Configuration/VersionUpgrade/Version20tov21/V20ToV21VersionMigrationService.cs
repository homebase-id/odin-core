using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Apps.Builtin;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.CircleMembership;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Configuration.VersionUpgrade.Version20tov21
{
    /// <summary>
    /// v20 → v21: puts Friends back to its built-in drive grants, and deletes the retired Work, Acquaintances
    /// and Family circles where nobody is in them (#1947).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Owners could once add drives to Friends -- public posts, a drive of their own.  Friends belongs to the
    /// Contacts app, which holds no storage key for those drives, so a pending enrollment into such a Friends
    /// can only be completed with the master key, and nothing but the owner console has it.  Those entries sat
    /// parked forever.  Owners can no longer add drives, so resetting the definition to
    /// <see cref="BuiltinCircles.FriendsCircle"/> ends it: members lose the extra drives, and every pending
    /// enrollment becomes one Contacts can finish.
    /// </para>
    /// <para>
    /// Work, Acquaintances and Family left the app tree.  An empty one is only clutter, so it is deleted; one
    /// with members is the owner's and is kept as it is.
    /// </para>
    /// </remarks>
    public class V20ToV21VersionMigrationService(
        ILogger<V20ToV21VersionMigrationService> logger,
        CircleNetworkService circleNetworkService,
        CircleDefinitionService circleDefinitionService,
        CircleMembershipService circleMembershipService)
    {
        /// <summary>
        /// Work, Acquaintances and Family, which left the app tree.  Their ids live only here (and in v19 → v20).
        /// </summary>
        public static readonly IReadOnlyList<Guid> RetiredCircleIds =
        [
            Guid.Parse("0f9263536b9fc61ada745644735bfd8f"), // Work
            Guid.Parse("55c53cfda992192581cb4f006109df47"), // Acquaintances
            Guid.Parse("cefc4f7cbc8c34762e0f76703e7e174e") // Family
        ];

        public async Task ResetFriendsDriveGrantsAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();
            cancellationToken.ThrowIfCancellationRequested();

            var def = await circleDefinitionService.GetCircleAsync(BuiltinCircles.FriendsCircle.Id);
            if (def == null)
            {
                logger.LogDebug("Friends circle not found; nothing to reset");
                return;
            }

            if (HasBuiltinDriveGrants(def))
            {
                return;
            }

            logger.LogInformation("Resetting Friends circle drive grants from [{from}] to the built-in definition",
                string.Join(", ", def.DriveGrants?.Select(g => g.PermissionedDrive.Drive.Alias) ?? []));

            def.DriveGrants = BuiltinCircles.FriendsCircle.DriveGrants.ToList();

            // Persists the definition and re-mints every member's grant, so the dropped drives go with it.
            await circleNetworkService.UpdateCircleDefinitionAsync(def, odinContext);
        }

        public async Task DeleteEmptyRetiredCirclesAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            odinContext.Caller.AssertHasMasterKey();

            foreach (var circleId in RetiredCircleIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var def = await circleDefinitionService.GetCircleAsync(circleId);
                if (def == null)
                {
                    continue;
                }

                var members = await circleMembershipService.GetDomainsInCircleAsync(circleId, odinContext, overrideHack: true);
                if (members.Count > 0)
                {
                    logger.LogInformation("Keeping circle {circle}: it has {count} member(s)", def.Name, members.Count);
                    continue;
                }

                logger.LogInformation("Deleting empty circle {circle}", def.Name);
                await circleNetworkService.DeleteCircleDefinitionAsync(circleId, odinContext);
            }
        }

        public async Task ValidateUpgradeAsync(IOdinContext odinContext, CancellationToken cancellationToken)
        {
            var friends = await circleDefinitionService.GetCircleAsync(BuiltinCircles.FriendsCircle.Id);
            if (friends != null && !HasBuiltinDriveGrants(friends))
            {
                throw new OdinSystemException("Friends circle does not have its built-in drive grants");
            }

            foreach (var circleId in RetiredCircleIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (await circleDefinitionService.GetCircleAsync(circleId) == null)
                {
                    continue;
                }

                var members = await circleMembershipService.GetDomainsInCircleAsync(circleId, odinContext, overrideHack: true);
                if (members.Count == 0)
                {
                    throw new OdinSystemException($"Retired circle {circleId} is empty but was not deleted");
                }
            }
        }

        private static bool HasBuiltinDriveGrants(CircleDefinition def)
        {
            // Compared with ==, not a set: PermissionedDrive.Equals looks at the drive alone, so a grant whose
            // permission was widened would read as unchanged.
            var actual = (def.DriveGrants ?? []).Select(g => g.PermissionedDrive).ToList();
            var expected = BuiltinCircles.FriendsCircle.DriveGrants.Select(g => g.PermissionedDrive).ToList();
            return actual.Count == expected.Count &&
                   expected.All(e => actual.Any(a => a == e)) &&
                   actual.All(a => expected.Any(e => e == a));
        }
    }
}
