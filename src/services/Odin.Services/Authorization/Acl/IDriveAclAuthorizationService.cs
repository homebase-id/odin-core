using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Membership.Connections;

namespace Odin.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task<bool> IdentityHasPermission(IdentityConnectionRegistration recipientIcr, AccessControlList acl);
        
        Task AssertCallerHasPermission(AccessControlList acl);

        Task<bool> CallerHasPermission(AccessControlList acl);
    }
}