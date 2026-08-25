using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl
{
    public class DriveAclAuthorizationService(
        CircleNetworkService circleNetwork,
        ILogger<DriveAclAuthorizationService> logger)
        : IDriveAclAuthorizationService
    {
        public async Task AssertCallerHasPermission(AccessControlList acl, IOdinContext odinContext)
        {
            ThrowWhenFalse(await CallerHasPermission(acl, odinContext));
        }

        public async Task<bool> IdentityHasPermissionAsync(OdinId odinId, AccessControlList acl, IOdinContext odinContext)
        {
            //there must be an acl
            if (acl == null)
            {
                return false;
            }

            //if file has required circles, see if caller has at least one
            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Any())
            {
                var icr = await circleNetwork.GetIcrAsync(odinId, odinContext, true);
                var hasBadData = icr.PeerKeyStore.CircleGrants?.Where(cg => cg.Value?.CircleId?.Value == null).Any();
                if (hasBadData.GetValueOrDefault())
                {
                    var cg = icr.PeerKeyStore.CircleGrants?.Select(cg => cg.Value.Redacted());
                    logger.LogInformation("ICR for {odinId} has corrupt circle grants. {cg}", odinId, cg);

                    //let it continue on
                }

                var hasAtLeastOneCircle = requiredCircles.Intersect(icr.PeerKeyStore.CircleGrants?.Select(cg => cg.Value.CircleId.Value) ?? Array.Empty<Guid>())
                    .Any();
                return hasAtLeastOneCircle;
            }

            if (acl.GetRequiredIdentities().Any())
            {
                return false;
            }

            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return true;

                // The identity-based path, used for outbound distribution.  It has to test the review
                // rather than the connection: a Reviewed ACL admits reviewed people, and this path never
                // sees a caller context to read the level from.
                case SecurityGroupType.Reviewed:
                    return (await circleNetwork.GetIcrAsync(odinId, odinContext, true)).IsReviewed();
            }

            return false;
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

            //there must be an acl
            if (acl == null)
            {
                return Task.FromResult(false);
            }

            //if file has required circles, see if caller has at least one
            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Any() && !requiredCircles.Intersect(caller!.Circles.Select(c => c.Value)).Any())
            {
                return Task.FromResult(false);
            }

            if (acl.GetRequiredIdentities().Any())
            {
                throw new NotImplementedException("TODO: enforce logic for required identities");
            }

            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return Task.FromResult(true);

                case SecurityGroupType.Authenticated:
                    return Task.FromResult(((int)caller!.SecurityLevel) >= (int)SecurityGroupType.Authenticated);

                // Legacy 555 files (the chat app ACLs messages with it) evaluate at the same threshold as
                // 777, exactly as they did when the two shared a case.  Dropping the case would deny them
                // to every caller including a reviewed one, and would disagree with the index filter --
                // requiredSecurityGroup BETWEEN 0 AND callerLevel admits 555 to a 777 caller, so the file
                // would list and then fail to read.
                case SecurityGroupType.AutoConnected:
                case SecurityGroupType.Reviewed:
                    return Task.FromResult(((int)caller!.SecurityLevel) >= (int)SecurityGroupType.Reviewed);
            }

            return Task.FromResult(false);
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