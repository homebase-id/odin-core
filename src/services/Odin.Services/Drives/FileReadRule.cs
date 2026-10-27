#nullable enable
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Drives;

/// <summary>
/// Whether the caller may see a stored file, once it may read the file's drive: the file's ACL, and a
/// zero-knowledge drive's own file only for a caller holding the drive's key (<see cref="ZkDriveFiles.VisibleTo"/>).
/// A read by id, a query and a socket all ask this one question, so none of them shows a file another would
/// hide.  How a refusal answers is each path's own: a read by id refuses, a query leaves the file out.  What a visible file returns (its key header, a storage key it needs) is answered where it is returned.
/// </summary>
/// <remarks>No header has no ACL, which only the owner and the system pass (a drive event without one).</remarks>
public static class FileReadRule
{
    public static async Task<bool> CallerMayReadAsync(this IDriveAclAuthorizationService aclService, ServerFileHeader? header,
        IOdinContext odinContext) =>
        ZkDriveFiles.VisibleTo(header, odinContext) &&
        await aclService.CallerHasPermission(header?.ServerMetadata?.AccessControlList!, odinContext);

    public static async Task AssertCallerMayReadAsync(this IDriveAclAuthorizationService aclService, ServerFileHeader header,
        IOdinContext odinContext)
    {
        if (!await aclService.CallerMayReadAsync(header, odinContext))
        {
            throw new OdinSecurityException("No access permitted to the file");
        }
    }
}
