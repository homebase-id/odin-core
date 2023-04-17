using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Youverse.Core.Services.Base;

namespace Youverse.Core.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task AssertCallerHasPermission(AccessControlList acl);

        Task<bool> CallerHasPermission(AccessControlList acl);

        Task<bool> CallerHasPermission(CallerContext caller, AccessControlList acl);
        
        Task<bool> CallerIsInYouverseNetwork();

        Task<bool> CallerIsInList(List<string> odinIdList);

    }
}