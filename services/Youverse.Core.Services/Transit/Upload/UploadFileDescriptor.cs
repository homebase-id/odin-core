using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Transit.Upload
{
    public class UploadFileDescriptor
    {
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
        public UploadFileMetadata FileMetadata { get; set; }
    }
}