using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;

namespace Odin.Core.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task<bool> IdentityHasPermission(OdinId odinId, AccessControlList acl);
        
        Task AssertCallerHasPermission(AccessControlList acl);

        Task<bool> CallerHasPermission(AccessControlList acl);
    }
}