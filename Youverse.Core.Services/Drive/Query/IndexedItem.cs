using System;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Drive.Query
{
    public class IndexedItem
    {
        /// <summary>
        /// A unique record id for the indexed item
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// The created timestamp of the <see cref="FileId"/> in UnixTime milliseconds
        /// </summary>
        public UInt64 CreatedTimestamp { get; set; }

        /// <summary>
        /// The last updated timestamp of the <see cref="FileId"/> in UnixTime milliseconds
        /// </summary>
        public UInt64 LastUpdatedTimestamp { get; set; }

        public Guid CategoryId { get; set; }

        /// <summary>
        /// The FileId of the data stored on disk in <see cref="IStorageManager"/>
        /// </summary>
        public Guid FileId { get; set; }

        //TODO:what is this?
        //public string FileType { get; set; }

        /// <summary>
        /// A payload of data attached to the indexed item.  This
        /// can be used to store a small amount of data rather than
        /// having to query it from the original data source 
        /// </summary>
        public string JsonPayload { get; set; }
    }
}