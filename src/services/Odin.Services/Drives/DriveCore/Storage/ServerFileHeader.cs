using System;
using System.Collections.Generic;
using Odin.Services.Peer.Encryption;

namespace Odin.Services.Drives.DriveCore.Storage
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


    public class BatchUpdateManifest
    {
        public Guid NewVersionTag { get; set; }
        public List<PayloadDescriptor> PayloadDescriptors { get; set; }
    }
}