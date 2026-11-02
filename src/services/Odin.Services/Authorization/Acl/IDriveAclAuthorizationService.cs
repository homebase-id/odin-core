using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Services.Base;
using Odin.Services.Drives;
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
        /// <param name="sharedWithCaller">On a Selective Share drive, the caller's share rows for the page
        /// (<see cref="FilesSharedWithCallerAsync"/>), so a page costs one read; read for this file when not given.</param>
        Task<bool> CallerMayReadAsync(ServerFileHeader header, IOdinContext odinContext, CallerShares sharedWithCaller = null);

        /// <summary>
        /// On a Selective Share drive, the share rows naming the caller for <paramref name="headers"/>, in one read: a
        /// file's own, a comment's those of the file it comments on (docs/selective-share-pr3-plan.md, section 10).  Null
        /// when the caller's reads don't depend on rows: an owner caller, the System, or any other drive.
        /// </summary>
        Task<CallerShares> FilesSharedWithCallerAsync(Guid driveId, IReadOnlyCollection<ServerFileHeader> headers,
            IOdinContext odinContext);

        /// <summary>
        /// On a Selective Share drive, whether the caller may do what <paramref name="permission"/> names to the file of
        /// <paramref name="header"/>: an owner caller, or a member with that bit both on the drive and in a live share,
        /// whose level reaches the file's group as for a read: a share row narrows its circles' grant (docs/file-access.md,
        /// "A share row narrows its circle's grant").  A comment goes by the file it comments on.
        /// </summary>
        Task<bool> CallerHoldsShareAsync(ServerFileHeader header, DrivePermission permission, IOdinContext odinContext);
    }
}