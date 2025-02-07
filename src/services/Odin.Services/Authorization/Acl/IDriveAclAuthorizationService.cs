using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task<bool> IdentityHasPermissionAsync(OdinId odinId, AccessControlList acl, IOdinContext odinContext);
        
        Task AssertCallerHasPermission(AccessControlList acl, IOdinContext odinContext);

        Task<bool> CallerHasPermission(AccessControlList acl, IOdinContext odinContext);
    }
}