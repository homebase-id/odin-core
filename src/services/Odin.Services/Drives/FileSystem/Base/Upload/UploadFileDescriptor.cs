using System;
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

        /// <summary>
        /// A new file's global transit id, when the client picks it, so it can bind key material to the file before the
        /// server answers: a share's locked file key is bound to it (docs/selective-share-plan.md, "Blob formats").  The
        /// server picks one when unset.  Refused when the drive already has a file with it.  A new standard file's only,
        /// as <see cref="Shares"/> are.
        /// </summary>
        public Guid? GlobalTransitId { get; set; }
    }
}