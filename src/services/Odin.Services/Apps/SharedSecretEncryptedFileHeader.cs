using System;
using Odin.Core.Exceptions;
using Odin.Core.Identity;
using Odin.Core.Storage;
using Odin.Services.Drives;
using Odin.Services.Drives.DriveCore.Storage;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Apps
{
    public class SharedSecretEncryptedFileHeader
    {
        public Guid FileId { get; set; }

        public TargetDrive TargetDrive { get; set; }

        public Guid DriveId => this.TargetDrive.Alias;

        public FileState FileState { get; set; }

        public FileSystemType FileSystemType { get; set; }

        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }

        public ClientFileMetadata FileMetadata { get; set; }

        public ServerMetadata ServerMetadata { get; set; }

        public int Priority { get; set; }

        public Int64 FileByteCount { get; set; }

        public void AssertFileIsActive()
        {
            if (this.FileState == FileState.Deleted)
            {
                throw new OdinSecurityException($"File is deleted.");
            }
        }

        public void AssertOriginalAuthor(OdinId odinId)
        {
            if (string.IsNullOrEmpty(this.FileMetadata.OriginalAuthor))
            {
                // backwards compatibility
                AssertOriginalSender(odinId);
            }

            if (this.FileMetadata.OriginalAuthor != odinId)
            {
                throw new OdinSecurityException("Sender does not match original author");
            }
        }

        public bool IsOriginalSender(OdinId odinId)
        {
            return new OdinId(this.FileMetadata.SenderOdinId) == odinId;
        }

        public void AssertOriginalSender(OdinId odinId)
        {
            if (string.IsNullOrEmpty(this.FileMetadata.SenderOdinId))
            {
                throw new OdinSecurityException(
                    $"Original file does not have a sender (FileId: {this.FileId} on Drive: {this.TargetDrive}");
            }

            if (!IsOriginalSender(odinId))
            {
                throw new OdinSecurityException("Sender does not match original sender");
            }
        }
    }
}