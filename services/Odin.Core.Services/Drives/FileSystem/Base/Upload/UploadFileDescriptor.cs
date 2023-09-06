using Odin.Core.Services.Peer.Encryption;

namespace Odin.Core.Services.Drives.FileSystem.Base.Upload
{
    public class UploadFileDescriptor
    {
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
        public UploadFileMetadata FileMetadata { get; set; }
    }
}