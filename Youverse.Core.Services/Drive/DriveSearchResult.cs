using System;
using System.Collections.Generic;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Query;

namespace Youverse.Core.Services.Drive
{
    public class DriveSearchResult : IIndexedItem
    {
        public Guid FileId { get; set; }

        public List<Guid> Tags { get; set; }
        public int FileType { get; set; }
        
        public int DataType { get; set; }
        public bool ContentIsComplete { get; set; }
        public bool PayloadIsEncrypted { get; set; }
        public string JsonContent { get; set; }
        
        public Guid Alias { get; set; }

        public ulong CreatedTimestamp { get; set; }

        public string SenderDotYouId { get; set; }

        public ulong LastUpdatedTimestamp { get; set; }
        
        public long PayloadSize { get; set; }
        
        /// <summary>
        /// When true, the payload was too large to return
        /// </summary>
        public bool PayloadTooLarge { get; set; }
        public string PayloadContent { get; set; }
        public AccessControlList AccessControlList { get; set; }
        
        /// <summary>
        /// The lower the number, the higher the priority
        /// </summary>
        public int Priority { get; set; }
    }
}