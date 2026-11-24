using Odin.Core.Storage;
using System.Threading.Tasks;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Services.Apps;
using Odin.Services.Authorization.Acl;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;

namespace Odin.Services.Peer.Incoming.Drive.Transfer
{
    /// <summary>
    /// The ACL a peer's incoming file is stored under (<see cref="ForIncomingFileAsync"/>), and a comment's, its referenced
    /// file's. Shared by the new-file and update paths so the two answer the same way.
    /// </summary>
    internal static class IncomingFileAcl
    {
        /// <summary>
        /// The ACL a peer's file is stored with, new or updated: only the owner's, so the owner can pass it along; a
        /// comment its file's (<see cref="ResetAclForComment"/>); a post on a community channel the one its sender gave it.
        /// </summary>
        public static async Task<AccessControlList> ForIncomingFileAsync(FileSystemResolver fileSystemResolver,
            FileSystemType fileSystemType, FileMetadata metadata, bool isCollaborationChannel, AccessControlList originalAcl,
            IOdinContext odinContext)
        {
            if (fileSystemType == FileSystemType.Comment)
            {
                return await ResetAclForComment(fileSystemResolver, metadata, odinContext);
            }

            return isCollaborationChannel ? originalAcl ?? AccessControlList.OwnerOnly : AccessControlList.OwnerOnly;
        }

        /// <summary>
        /// The ACL <paramref name="sender"/>'s change of <paramref name="stored"/> leaves it with, given the one
        /// <see cref="ForIncomingFileAsync"/> chose.  On a community channel a post keeps its stored ACL: the sender's copy
        /// carries no named identities, and a member's app could name any group, so taking it would widen the post.  Only
        /// the post's original sender changes it, and only to one no wider (#2002).  Elsewhere, and for a comment, which
        /// takes its file's, the one chosen.
        /// </summary>
        public static AccessControlList ForChange(StorageDrive drive, FileSystemType fileSystemType, AccessControlList incoming,
            ServerFileHeader stored, OdinId sender)
        {
            if (fileSystemType == FileSystemType.Comment || !drive.IsCollaborationDrive())
            {
                return incoming;
            }

            var storedAcl = stored.ServerMetadata.AccessControlList ?? AccessControlList.OwnerOnly;
            return SharedSecretEncryptedFileHeader.IsOriginalSender(stored.FileMetadata.SenderOdinId, sender) &&
                   incoming.IsNoWiderThan(storedAcl)
                ? incoming
                : storedAcl;
        }

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
                // Resolved above, yet withheld: expired but not yet tombstoned, deleted in between, or
                // encrypted and the caller has no storage key. (A caller the ACL denies gets a security
                // exception instead.) None is fixed by a resend, so a 400 rather than a retried 503.
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
