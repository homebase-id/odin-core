using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    public class UploadFileDescriptor
    {
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
        public UploadFileMetadata FileMetadata { get; set; }
    }
}