using System;
using Youverse.Core.Exceptions;
using Youverse.Core.Identity;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Drives;
using Youverse.Core.Services.Drives.FileSystem;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class SharedSecretEncryptedFileHeader
    {
        public Guid FileId { get; set; }
        
        public FileState FileState { get; set; }
        
        public FileSystemType FileSystemType { get; set; } 
        
        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }

        public ClientFileMetadata FileMetadata { get; set; }
        
        public ServerMetadata ServerMetadata { get; set; }
        
        public int Priority { get; set; }
        
        public ReactionPreviewData ReactionPreview { get; set; }

        public void AssertFileIsActive()
        {
            if (this.FileState == FileState.Deleted)
            {
                throw new YouverseSecurityException($"File is deleted.");
            }
        }

        public void AssertOriginalSender(DotYouIdentity dotYouId, string message)
        {
            if (new DotYouIdentity(this.FileMetadata.SenderDotYouId) != dotYouId)
            {
                throw new YouverseSecurityException(message);
            }
        }
    }
}