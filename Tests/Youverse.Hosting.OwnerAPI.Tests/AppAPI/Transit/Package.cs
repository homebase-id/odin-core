using System;
using System.IO;
using Youverse.Core.Services.Transit.Encryption;
using Youverse.Core.Services.Transit.Upload;

namespace Youverse.Hosting.Tests.AppAPI.Transit
{
    public class Package
    {
        public Guid Id { get; set; }
        
        public RecipientList RecipientList { get; set; }

        public EncryptedKeyHeader TransferEncryptedKeyHeader { get; set; }
        public Stream Metadata { get; set; }
        public Stream Payload { get; set; }
        
    }

}