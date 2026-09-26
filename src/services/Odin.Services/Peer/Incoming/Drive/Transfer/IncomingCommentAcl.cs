using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    /// <summary>
    /// The ACL an incoming comment is stored under: its referenced file's. Shared by the new-file and
    /// update paths so the two answer a bad comment the same way.
    /// </summary>
    internal static class IncomingCommentAcl
    {
        public static async Task<AccessControlList> ResetAclForComment(FileSystemResolver fileSystemResolver,
            FileMetadata metadata, IOdinContext odinContext)
        {
            var (referencedFs, fileId) = await fileSystemResolver.ResolveFileSystem(metadata.ReferencedFile, odinContext);

            if (null == referencedFs || !fileId.HasValue)
            {
                throw new OdinClientException("Referenced file missing or caller does not have access");
            }

            //
            // Issue - the caller cannot see the ACL because it's only shown to the
            // owner, so we need to forceIncludeServerMetadata
            //

            var referencedFile = await referencedFs.Query.GetFileByGlobalTransitId(fileId.Value.DriveId,
                metadata.ReferencedFile.GlobalTransitId, odinContext: odinContext, forceIncludeServerMetadata: true);

            if (null == referencedFile)
            {
                // Resolved above, so the caller most likely cannot read it. Like the S2040 case below, a
                // resend will not change that, so it is a 400 rather than a retried 503.
                throw new OdinClientException("Referenced file missing or caller does not have access",
                    OdinClientErrorCode.InvalidReferenceFile);
            }

            //S2040
            if (referencedFile.FileMetadata.IsEncrypted != metadata.IsEncrypted)
            {
                // The sender's data is wrong, and resending it will not change that: a 400 lets the
                // sender's outbox settle the item instead of retrying it as a 503 (#1771).
                throw new OdinClientException("Referenced file and metadata payload encryption do not match",
                    OdinClientErrorCode.InvalidReferenceFile);
            }

            return referencedFile.ServerMetadata.AccessControlList;
        }
    }
}
