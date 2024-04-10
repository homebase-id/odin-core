using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Odin.Core.Exceptions;
using Odin.Services.Base;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl
{
    public class DriveAclAuthorizationService(
        IOdinContextAccessor contextAccessor,
        ILogger<DriveAclAuthorizationService> logger)
        : IDriveAclAuthorizationService
    {
        public async Task AssertCallerHasPermission(AccessControlList acl)
        {
            ThrowWhenFalse(await CallerHasPermission(acl));
        }

        public async Task<bool> IdentityHasPermission(IdentityConnectionRegistration recipientIcr, AccessControlList acl)
        {
            //there must be an acl
            if (acl == null)
            {
                return await Task.FromResult(false);
            }

            var odinId = recipientIcr.OdinId;

            //if file has required circles, see if caller has at least one
            var requiredCircles = acl.GetRequiredCircles().ToList();
            if (requiredCircles.Any())
            {
                var hasBadData = recipientIcr.AccessGrant.CircleGrants?.Where(cg => cg.Value?.CircleId?.Value == null).Any();
                if (hasBadData.GetValueOrDefault())
                {
                    var cg = recipientIcr.AccessGrant.CircleGrants?.Select(cg => cg.Value.Redacted());
                    logger.LogWarning("ICR for {odinId} has corrupt circle grants. {cg}", odinId, cg);

                    //let it continue on
                }

                var hasAtLeastOneCircle = requiredCircles
                    .Intersect(recipientIcr.AccessGrant.CircleGrants?.Select(cg => cg.Value.CircleId.Value) ?? Array.Empty<Guid>())
                    .Any();
                return await Task.FromResult(hasAtLeastOneCircle);
            }

            if (acl.GetRequiredIdentities().Any())
            {
                return await Task.FromResult(false);
            }

            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return await Task.FromResult(true);

                case SecurityGroupType.Connected:
                    return await Task.FromResult(recipientIcr.IsConnected());
            }

            return await Task.FromResult(false);
        }

        public Task<bool> CallerHasPermission(AccessControlList acl)
        {
            var caller = contextAccessor.GetCurrent().Caller;
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

                case SecurityGroupType.Connected:
                    return CallerIsConnected();
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

        private async Task<bool> CallerIsConnected()
        {
            //TODO: cache result - 
            return await Task.FromResult(contextAccessor.GetCurrent().Caller.IsConnected);
        }
    }
}