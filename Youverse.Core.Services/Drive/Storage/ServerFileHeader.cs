﻿using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive.Storage
{
    public class ServerFileHeader
    {
        
        public EncryptedKeyHeader EncryptedKeyHeader { get; set; }
        
        public FileMetadata FileMetadata { get; set; }
        
        public ServerMetadata ServerMetadata { get; set; }

        public bool IsValid()
        {
            return this.EncryptedKeyHeader != null
                   && this.FileMetadata != null
                   && this.ServerMetadata != null;
        }
    }

}