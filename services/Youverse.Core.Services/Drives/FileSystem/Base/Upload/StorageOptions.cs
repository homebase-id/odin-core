using System;

namespace Youverse.Core.Services.Drives.FileSystem.Base.Upload
{
    /// <summary>
    /// Defines the options for storage
    /// </summary>
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
        
        // public bool IgnoreMissingReferencedFile { get; set; }
    }
}