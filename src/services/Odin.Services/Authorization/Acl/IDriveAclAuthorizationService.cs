using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task<bool> IdentityHasPermission(OdinId odinId, AccessControlList acl, OdinContext odinContext);
        
        Task AssertCallerHasPermission(AccessControlList acl, OdinContext odinContext);

        Task<bool> CallerHasPermission(AccessControlList acl, OdinContext odinContext);
    }
}