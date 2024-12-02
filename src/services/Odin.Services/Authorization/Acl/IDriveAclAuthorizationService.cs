using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task<bool> IdentityMatchesAclAsync(Guid driveId,OdinId odinId, AccessControlList acl, IOdinContext odinContext);
        
        Task AssertCallerMatchesAclAsync(Guid driveId, AccessControlList acl, IOdinContext odinContext);

        Task<bool> CallerMatchesAclAsync(Guid driveId, AccessControlList appliedAcl, IOdinContext odinContext);
    }
}