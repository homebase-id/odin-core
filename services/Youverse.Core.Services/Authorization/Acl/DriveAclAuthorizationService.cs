using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dawn;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Acl
{
    public class DriveAclAuthorizationService : IDriveAclAuthorizationService
    {
        private readonly DotYouContextAccessor _contextAccessor;

        public DriveAclAuthorizationService(DotYouContextAccessor contextAccessor)
        {
            _contextAccessor = contextAccessor;
        }

        public Task AssertCallerHasPermission(AccessControlList acl)
        {
            ThrowWhenFalse(CallerHasPermission(acl).GetAwaiter().GetResult());

            return Task.CompletedTask;
        }

        public Task<bool> CallerHasPermission(AccessControlList acl)
        {
            return CallerHasPermission(_contextAccessor.GetCurrent().Caller, acl);
        }

        public Task<bool> CallerHasPermission(CallerContext caller, AccessControlList acl)
        {
            Guard.Argument(caller, nameof(caller)).NotNull();

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

            var authorizedIdentities = acl.GetRequiredIdentities().ToList();
            bool requiresAuthorizedIdentity = authorizedIdentities.Any();
            bool hasRequiredIdentity = false;
            if (requiresAuthorizedIdentity)
            {
                //does the caller match one of these identities
                hasRequiredIdentity = authorizedIdentities.Any(id => (OdinId)id == caller!.OdinId);
            }

            bool matchesSecurityGroup = false;
            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return Task.FromResult(true);

                case SecurityGroupType.Authenticated:
                    matchesSecurityGroup = caller!.IsInYouverseNetwork;
                    break;

                case SecurityGroupType.Connected:
                    matchesSecurityGroup = _contextAccessor.GetCurrent().Caller.IsConnected;
                    break;
            }

            if (requiresAuthorizedIdentity)
            {
                return Task.FromResult(hasRequiredIdentity && matchesSecurityGroup);
            }

            return Task.FromResult(matchesSecurityGroup);
        }

        public Task<bool> CallerIsInYouverseNetwork()
        {
            return Task.FromResult(_contextAccessor.GetCurrent().Caller.IsInYouverseNetwork);
        }

        public Task<bool> CallerIsInList(List<string> odinIdList)
        {
            var inList = odinIdList.Any(s =>
                s.Equals(_contextAccessor.GetCurrent().GetCallerOdinIdOrFail().DomainName, StringComparison.InvariantCultureIgnoreCase));
            return Task.FromResult(inList);
        }

        private void ThrowWhenFalse(bool eval)
        {
            if (eval == false)
            {
                throw new YouverseSecurityException();
            }
        }
    }
}