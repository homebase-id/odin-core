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

        public void AssertOriginalSender(OdinId odinId, string message)
        {
            if (new OdinId(this.FileMetadata.SenderOdinId) != odinId)
            {
                throw new OdinSecurityException(message);
            }
        }
    }
}