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
        private readonly ICircleNetworkService _circleNetwork;

        public DriveAclAuthorizationService(DotYouContextAccessor contextAccessor, ICircleNetworkService circleNetwork, IHttpContextAccessor httpContext)
        {
            _contextAccessor = contextAccessor;
            _circleNetwork = circleNetwork;
        }

        public Task AssertCallerHasPermission(AccessControlList acl)
        {
            //ThrowWhenFalse(CallerHasPermission(acl).GetAwaiter().GetResult());

            return Task.CompletedTask;
        }

        public Task<bool> CallerHasPermission(AccessControlList acl)
        {
            var caller = _contextAccessor.GetCurrent().Caller;
            if (caller?.IsOwner ?? false)
            {
                return Task.FromResult(true);
            }

            //there must be an acl
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

        public async Task<bool> CallerIsConnected()
        {
            //TODO: cache result - 
            var isConnected = await _circleNetwork.IsConnected(_contextAccessor.GetCurrent().Caller.DotYouId);
            return isConnected;
        }

        public Task<bool> CallerIsInYouverseNetwork()
        {
            return Task.FromResult(_contextAccessor.GetCurrent().Caller.IsInYouverseNetwork);
        }

        public Task<bool> CallerIsInList(List<string> dotYouIdList)
        {
            var inList = dotYouIdList.Any(s => s.Equals(_contextAccessor.GetCurrent().Caller.DotYouId.Id, StringComparison.InvariantCultureIgnoreCase));
            return Task.FromResult(inList);
        }

        public Task<bool> CallerIsInCircle(Guid? circleId)
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