using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;
using Youverse.Core.Services.Contacts.Circle;
using Youverse.Core.Services.Contacts.Circle.Membership;

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
            var caller = _contextAccessor.GetCurrent().Caller;
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
                    return Task.FromResult(caller!.IsInYouverseNetwork);

                case SecurityGroupType.Connected:
                    return CallerIsConnected();
            }

            return Task.FromResult(false);
        }

        public async Task<bool> CallerIsConnected()
        {
            //TODO: cache result - 
            return _contextAccessor.GetCurrent().Caller.IsConnected;
        }

        public Task<bool> CallerIsInYouverseNetwork()
        {
            return Task.FromResult(_contextAccessor.GetCurrent().Caller.IsInYouverseNetwork);
        }

        public Task<bool> CallerIsInList(List<string> odinIdList)
        {
            var inList = odinIdList.Any(s => s.Equals(_contextAccessor.GetCurrent().GetCallerOdinIdOrFail().DomainName, StringComparison.InvariantCultureIgnoreCase));
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