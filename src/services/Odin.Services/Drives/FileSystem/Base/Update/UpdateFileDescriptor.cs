﻿using Odin.Services.Drives.FileSystem.Base.Upload;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives.FileSystem.Base.Update
{
    public class UpdateFileDescriptor
    {
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }

        // /// <summary>
        // /// The new IV used on the key header
        // /// </summary>
        // public byte[] KeyHeaderIv { get; init; }

        public UploadFileMetadata FileMetadata { get; init; }
    }
}