using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Odin.Core.Identity;
using Odin.Core.Storage.Database.Identity.Table;
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
        /// <param name="sharedWithCaller">On a Selective Share drive, the files of the page the caller holds a live
        /// share row for, with the row (<see cref="FilesSharedWithCallerAsync"/>), so a page costs one read; read for
        /// this file when not given.</param>
        Task<bool> CallerMayReadAsync(ServerFileHeader header, IOdinContext odinContext,
            IReadOnlyDictionary<Guid, DriveAclIndexRecord> sharedWithCaller = null);

        /// <summary>
        /// On a Selective Share drive, which of <paramref name="fileIds"/> the caller holds a live share row for, in one
        /// read (docs/file-access.md, "live row"), each with the row their reads go by (the lowest circle id's).  Null
        /// when the caller's reads don't depend on rows: an owner caller, the System, or any other drive.
        /// </summary>
        Task<IReadOnlyDictionary<Guid, DriveAclIndexRecord>> FilesSharedWithCallerAsync(Guid driveId, IReadOnlyCollection<Guid> fileIds,
            IOdinContext odinContext);
    }
}