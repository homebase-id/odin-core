using System;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Base;

namespace Odin.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task<bool> IdentityMatchesAclAsync(Guid driveId, OdinId odinId, AccessControlList appliedAcl, IOdinContext odinContext,
            IdentityDatabase db);

        Task AssertCallerMatchesAclAsync(Guid driveId, AccessControlList acl, IdentityDatabase db, IOdinContext odinContext);

        Task<bool> CallerMatchesAclAsync(Guid driveId, AccessControlList appliedAcl, IdentityDatabase db, IOdinContext odinContext);
    }
}