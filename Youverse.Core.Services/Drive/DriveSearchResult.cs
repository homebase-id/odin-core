using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Storage;
using Youverse.Core.Services.Transit.Encryption;

namespace Youverse.Core.Services.Drive
{
    public class DriveSearchResult
    {
        
        public EncryptedKeyHeader SharedSecretEncryptedKeyHeader { get; set; }
        
        public Guid FileId { get; set; }

        public List<byte[]> Tags { get; set; }
        public int FileType { get; set; }
        
        public byte[] ThreadId { get; set; }
        public int DataType { get; set; }
        public bool ContentIsComplete { get; set; }
        public bool PayloadIsEncrypted { get; set; }
        public string JsonContent { get; set; }
        
        public ulong CreatedTimestamp { get; set; }

        public string SenderDotYouId { get; set; }

        public ulong UserDate { get; set; }
        public ulong LastUpdatedTimestamp { get; set; }
        
        public AccessControlList AccessControlList { get; set; }
        
        /// <summary>
        /// The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }

        public string ContentType { get; set; }
        public IEnumerable<ThumbnailHeader> AdditionalThumbnails { get; set; }
        public ThumbnailContent PreviewThumbnail { get; set; }
    }
}