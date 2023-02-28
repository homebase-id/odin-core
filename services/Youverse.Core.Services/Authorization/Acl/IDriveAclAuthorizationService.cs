using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Youverse.Core.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task AssertCallerHasPermission(AccessControlList acl);

        Task<bool> CallerHasPermission(AccessControlList acl);

        Task<bool> CallerIsConnected();

        Task<bool> CallerIsInYouverseNetwork();

        Task<bool> CallerIsInList(List<string> odinIdList);

    }
}