using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Membership.Circles;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl
{
    public class DriveAclAuthorizationService(
        CircleNetworkService circleNetwork,
        CircleDefinitionService circleDefinitionService,
        TenantContext tenantContext)
        : IDriveAclAuthorizationService
    {
        /// <summary>
        /// Whether <paramref name="odinId"/> could read a file with this ACL if they called in themselves:
        /// the same decision as <see cref="CallerHasPermission"/>, from their connection record instead of a
        /// caller context.  Used where we send to someone rather than answer them (feed distribution).
        /// </summary>
        public async Task<bool> IdentityHasPermissionAsync(OdinId odinId, AccessControlList acl, IOdinContext odinContext)
        {
            if (acl == null)
            {
                return false;
            }

            // Anyone we send to is admitted at least at Authenticated, so an ACL asking no more than that,
            // and naming no circles, is decided without reading their connection record.
            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Count == 0 && (int)acl.RequiredSecurityGroup <= (int)SecurityGroupType.Authenticated)
            {
                return Allows(acl, SecurityGroupType.Authenticated, [], odinId);
            }

            var icr = await circleNetwork.GetIcrAsync(odinId, odinContext, overrideHack: true, tryUpgradeEncryption: false);

            // Anyone not connected is admitted at Authenticated, as a peer calling in unconnected is.
            if (!icr.IsConnected())
            {
                return Allows(acl, SecurityGroupType.Authenticated, [], odinId);
            }

            // A connection at Connected, lowered to Authenticated until reviewed (ReviewedSecurityTier), holding
            // its enabled circles only, as a caller's circles are: a disabled circle grants nothing.  Only the
            // circles the ACL names can matter.
            var circles = new List<Guid>();
            foreach (var circleId in (icr.PeerKeyStore?.CircleGrants.Keys ?? Enumerable.Empty<Guid>()).Intersect(requiredCircles))
            {
                if (await circleDefinitionService.IsEnabledAsync(circleId))
                {
                    circles.Add(circleId);
                }
            }

            return Allows(acl, ReviewedSecurityTier.For(tenantContext.DataVersionNumber, icr), circles, odinId);
        }

        public Task<bool> CallerHasPermission(AccessControlList acl, IOdinContext odinContext)
        {
            var caller = odinContext.Caller;
            if (caller?.IsOwner ?? false)
            {
                return Task.FromResult(true);
            }

            if (caller?.SecurityLevel == SecurityGroupType.System)
            {
                return Task.FromResult(true);
            }

            if (acl == null || caller == null)
            {
                return Task.FromResult(false);
            }

            var level = ReviewedSecurityTier.EffectiveLevel(tenantContext, caller);
            return Task.FromResult(Allows(acl, level, caller.Circles?.Select(c => c.Value).ToList() ?? [], caller.OdinId));
        }

        /// <summary>
        /// The one ACL decision, for <paramref name="reader"/> (null when anonymous) at <paramref name="level"/>
        /// holding <paramref name="circles"/>.
        /// </summary>
        /// <remarks>
        /// The security groups are a ladder -- Anonymous &lt; Authenticated &lt; AutoConnected &lt; Connected &lt;
        /// Owner -- and a reader meets every rung at or below their own, the same rule drive queries apply
        /// (<c>DriveQuery</c>, <c>IntRange(0, level)</c>).  Owner and System as callers never get here: they are
        /// shortcuts in front.  Named circles and named identities narrow the ACL further, together: the reader
        /// must be in one of the circles <em>or</em> be one of the identities, the same rule drive queries apply
        /// (<c>aclAnyOf</c>).  Neither replaces the security group.
        /// </remarks>
        public static bool Allows(AccessControlList acl, SecurityGroupType level, IReadOnlyCollection<Guid> circles, OdinId? reader)
        {
            // System is a caller level, never a file's ACL; refuse it rather than let its value (1) admit everyone.
            if (acl.RequiredSecurityGroup == SecurityGroupType.System)
            {
                return false;
            }

            if ((int)level < (int)acl.RequiredSecurityGroup)
            {
                return false;
            }

            var requiredCircles = acl.GetRequiredCircles().ToList();
            var requiredIdentities = acl.GetRequiredIdentities().ToList();
            if (requiredCircles.Count == 0 && requiredIdentities.Count == 0)
            {
                return true;
            }

            return requiredCircles.Intersect(circles).Any() ||
                   (reader.HasValue && requiredIdentities.Any(id => OdinId.IsValid(id) && (OdinId)id == reader.Value));
        }
    }
}