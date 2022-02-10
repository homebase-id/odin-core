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

        Task AssertCallerIsConnected();

        Task AssertCallerIsInYouverseNetwork();

        Task AssertCallerIsInList(List<string> dotYouIdList);

        Task AssertCallerIsInCircle(Guid circleId);
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
            var caller = _context.Caller;
            if (caller.IsOwner)
            {
                return Task.CompletedTask;
            }

            switch (acl.RequiredSecurityGroup)
            {
                case SecurityGroupType.Anonymous:
                    return Task.CompletedTask;

                case SecurityGroupType.YouAuthOrTransitCertificateIdentified:
                    ThrowIfFalse(caller.IsInYouverseNetwork);
                    break;

                case SecurityGroupType.Connected:
                    AssertCallerIsConnected();
                    break;

                case SecurityGroupType.CircleConnected:
                    AssertCallerIsConnected();
                    AssertCallerIsInCircle(acl.CircleId);
                    break;

                case SecurityGroupType.CustomList:
                    AssertCallerIsInYouverseNetwork();
                    AssertCallerIsInList(acl.DotYouIdentityList);
                    break;

                default:
                    ThrowIfFalse(false);
                    break;
            }

            return Task.CompletedTask;
        }

        public Task AssertCallerIsConnected()
        {
            //TODO: look up list of connections and cache
            var isConnected = false;
            ThrowIfFalse(isConnected);

            return Task.CompletedTask;
        }

        public Task AssertCallerIsInYouverseNetwork()
        {
            var isConnected = false;
            ThrowIfFalse(isConnected);

            return Task.CompletedTask;
        }

        public Task AssertCallerIsInList(List<string> dotYouIdList)
        {
            throw new NotImplementedException();
        }

        public Task AssertCallerIsInCircle(Guid circleId)
        {
            throw new NotImplementedException();
        }

        private void ThrowIfFalse(bool eval)
        {
            if (eval == false)
            {
                throw new YouverseSecurityException();
            }
        }
    }
}