using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Authorization.Acl
{
    public interface IDriveAclAuthorizationService
    {
        Task<bool> IdentityHasPermissionAsync(OdinId odinId, AccessControlList acl, IOdinContext odinContext);

        /// <summary>
        /// Whether the caller may see a stored file, once it may read the file's drive: the file's ACL, and a
        /// zero-knowledge drive's own file only for a caller holding the drive's key
        /// (<see cref="Drives.ZkDriveFiles.VisibleTo"/>).  A read by id, a query and a socket all ask this one
        /// question, so none of them shows a file another would hide; how a refusal answers is each path's own
        /// (a read by id refuses, a query leaves the file out).  What a visible file returns (its key header, a
        /// storage key it needs) is answered where it is returned.  No header has no ACL, which only the owner and
        /// the system pass (a drive event without one).
        /// </summary>
        Task<bool> CallerMayReadAsync(ServerFileHeader header, IOdinContext odinContext);
    }
}