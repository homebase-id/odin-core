using System;
using Youverse.Core.Services.Drive;

namespace Youverse.Core.Services.Transit.Upload
{
    public class StorageOptions
    {
        /// <summary>
        /// The drive in which to store this file
        /// </summary>
        public TargetDrive Drive { get; set; }

        /// <summary>
        /// The fileId to overwrite if it exists
        /// </summary>
        public Guid? OverwriteFileId { get; set; }

        /// <summary>
        /// Seconds in unix time UTC indicating when this file expires 
        /// </summary>
        public UInt64? ExpiresTimestamp { get; set; }

        /// <summary>
        /// Specifies the operations you expect when updating an existing file.  This is only used when OverWriteFileId is not null
        /// </summary>
        // public UpdateOperation UpdateFlags { get; set; } = UpdateOperation.None;
    }

    [Flags]
    public enum UpdateOperation
    {
        None = 0,
        UpdateMetadata = 1,
        UpdatePayload = 2,
        UpdateThumbnails = 4,
        UpdateAll = UpdateMetadata | UpdatePayload | UpdateThumbnails
    }
}