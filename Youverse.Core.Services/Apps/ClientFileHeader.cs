using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Apps
{
    public class ClientFileHeader
    {
        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }

        public FileMetadata FileMetadata { get; set; }
        
        public ServerMetadata ServerMetadata { get; set; }
        
    }
}