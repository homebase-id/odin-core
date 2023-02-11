using System;
using Youverse.Core.Services.Drive.Core.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class ClientFileHeader
    {
        public Guid FileId { get; set; }
        
        public FileState FileState { get; set; }
        
        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }

        public ClientFileMetadata FileMetadata { get; set; }
        
        public ServerMetadata ServerMetadata { get; set; }
        
        public int Priority { get; set; }

    }
}