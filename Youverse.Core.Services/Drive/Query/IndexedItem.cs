using System;
using System.Collections.Generic;
using LiteDB;
using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive.Query
{
    public interface IIndexedItem : IAppFileMetaData
    {
        /// <summary>
        /// The FileId of the data stored on disk in <see cref="IDriveService"/>
        /// </summary>
        Guid FileId { get; set; }

        /// <summary>
        /// The created timestamp of the <see cref="FileId"/> in UnixTime milliseconds
        /// </summary>
        UInt64 CreatedTimestamp { get; set; }

        /// <summary>
        /// The DotYouId of the sender of this file.
        /// </summary>
        string SenderDotYouId { get; set; }

        /// <summary>
        /// The last updated timestamp of the <see cref="FileId"/> in UnixTime milliseconds
        /// </summary>
        UInt64 LastUpdatedTimestamp { get; set; }
    }

    public class IndexedItem : IIndexedItem
    {
        /// <summary>
        /// The FileId of the data stored on disk in <see cref="IDriveService"/>
        /// </summary>
        [BsonId]
        public Guid FileId { get; set; }

        /// <summary>
        /// The created timestamp of the <see cref="FileId"/> in UnixTime milliseconds
        /// </summary>
        public UInt64 CreatedTimestamp { get; set; }
        
        /// <summary>
        /// The DotYouId of the sender of this file.
        /// </summary>
        public string SenderDotYouId { get; set; }

        /// <summary>
        /// The last updated timestamp of the <see cref="FileId"/> in UnixTime milliseconds
        /// </summary>
        public UInt64 LastUpdatedTimestamp { get; set; }

        public List<Guid> Tags { get; set; }
        
        public int FileType { get; set; }

        /// <summary>
        /// If true, the <see cref="JsonContent"/> is the full payload of information, otherwise, it is partial (like a preview of a chat message)
        /// </summary>
        public bool ContentIsComplete { get; set; }
        
        /// <summary>
        /// The JsonPayload to be included in the index.  This is not searchable but rather available to be returned
        /// when querying the index so you do not have to retrieve the whole payload
        /// </summary>
        public string JsonContent { get; set; }
        
        public bool PayloadIsEncrypted { get; set; }
        
        public AccessControlList AccessControlList { get; set; }
    }
}