using System.Collections.Generic;
using Odin.Services.Drives.Shares;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives.FileSystem.Base.Upload
{
    public class UploadFileDescriptor
    {
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
        public UploadFileMetadata FileMetadata { get; set; }

        /// <summary>
        /// A new file's shares on a Selective Share drive, next to its key header: key material together, encrypted with
        /// the shared secret (docs/selective-share-pr3-plan.md, section 4).  They name no file: they are this one's.
        /// </summary>
        public List<ShareRequest> Shares { get; set; }
    }
}