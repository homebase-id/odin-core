using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Exceptions;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Acl
{
    public interface IAuthorizationService
    {
        Task AssertCallerHasPermission(AccessControlList acl);

        Task<bool> CallerHasPermission(AccessControlList acl);

        Task<bool> CallerIsConnected();

        Task<bool> CallerIsInYouverseNetwork();

        Task<bool> CallerIsInList(List<string> dotYouIdList);

        Task<bool> CallerIsInCircle(Guid circleId);
    }

    public class AuthorizationService : IAuthorizationService
    {
        private readonly DotYouContext _context;

        public AuthorizationService(DotYouContext context)
        {
            _context = context;
        }

        public Task AssertCallerHasPermission(AccessControlList acl)
        {
            ThrowWhenFalse(CallerHasPermission(acl).GetAwaiter().GetResult());

            return Task.CompletedTask;
        }

        public Task<bool> CallerHasPermission(AccessControlList acl)
        {
            var caller = _context.Caller;
            if (caller.IsOwner)
            {
                return Task.FromResult(true);
            }

            if (acl == null)
            {
                return Task.FromResult(false);
            }
            
            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return Task.FromResult(true);

                case SecurityGroupType.YouAuthOrTransitCertificateIdentified:
                    return Task.FromResult(caller.IsInYouverseNetwork);

                case SecurityGroupType.Connected:
                    return CallerIsConnected();

                case SecurityGroupType.CircleConnected:
                    return Task.FromResult(CallerIsConnected().GetAwaiter().GetResult() &&
                                           CallerIsInCircle(acl.CircleId).GetAwaiter().GetResult());

                case SecurityGroupType.CustomList:
                    return Task.FromResult(CallerIsInYouverseNetwork().GetAwaiter().GetResult() &&
                                           CallerIsInList(acl.DotYouIdentityList).GetAwaiter().GetResult());
            }

            return Task.FromResult(false);
        }

        public Task<bool> CallerIsConnected()
        {
            //TODO: look up list of connections and cache
            var isConnected = false;
            return Task.FromResult(isConnected);
        }

        public Task<bool> CallerIsInYouverseNetwork()
        {
            return Task.FromResult(_context.Caller.IsInYouverseNetwork);
        }

        public Task<bool> CallerIsInList(List<string> dotYouIdList)
        {
            throw new NotImplementedException();
        }

        public Task<bool> CallerIsInCircle(Guid circleId)
        {
            throw new NotImplementedException();
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