using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
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
        public async Task AssertCallerHasPermission(AccessControlList acl, IOdinContext odinContext)
        {
            ThrowWhenFalse(await CallerHasPermission(acl, odinContext));
        }

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

            var icr = await circleNetwork.GetIcrAsync(odinId, odinContext, true);

            // What they would be admitted as: a connection at Connected, lowered to Authenticated until
            // reviewed (ReviewedSecurityTier); anyone else at Authenticated, as a peer calling in unconnected is.
            var level = icr.IsConnected()
                ? ReviewedSecurityTier.For(tenantContext.DataVersionNumber, icr)
                : SecurityGroupType.Authenticated;

            // Enabled circles only, as a caller's circles are: a disabled circle grants nothing.
            var circles = new List<Guid>();
            if (icr.IsConnected())
            {
                foreach (var circleId in icr.PeerKeyStore?.CircleGrants.Keys ?? Enumerable.Empty<Guid>())
                {
                    if (await circleDefinitionService.IsEnabledAsync(circleId))
                    {
                        circles.Add(circleId);
                    }
                }
            }

            return Allows(acl, level, circles);
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
            return Task.FromResult(Allows(acl, level, caller.Circles?.Select(c => c.Value).ToList() ?? []));
        }

        /// <summary>
        /// The one ACL decision, for a reader at <paramref name="level"/> holding <paramref name="circles"/>.
        /// </summary>
        /// <remarks>
        /// The security groups are a ladder -- Anonymous &lt; Authenticated &lt; AutoConnected &lt; Connected &lt;
        /// Owner -- and a reader meets every rung at or below their own, the same rule drive queries apply
        /// (<c>DriveQuery</c>, <c>IntRange(0, level)</c>).  Owner and System as callers never get here: they are
        /// shortcuts in front.  A circle list narrows the ACL further; it does not replace the security group.
        /// Named identities are not supported, so they admit no one.
        /// </remarks>
        public static bool Allows(AccessControlList acl, SecurityGroupType level, IReadOnlyCollection<Guid> circles)
        {
            if (acl.GetRequiredIdentities().Any())
            {
                return false;
            }

            // System is a caller level, never a file's ACL; refuse it rather than let its value (1) admit everyone.
            if (acl.RequiredSecurityGroup == SecurityGroupType.System)
            {
                return false;
            }

            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Any() && !requiredCircles.Intersect(circles).Any())
            {
                return false;
            }

            return (int)level >= (int)acl.RequiredSecurityGroup;
        }

        private void ThrowWhenFalse(bool eval)
        {
            if (eval == false)
            {
                throw new OdinSecurityException("I'm throwing because it's false!");
            }
        }
    }
}