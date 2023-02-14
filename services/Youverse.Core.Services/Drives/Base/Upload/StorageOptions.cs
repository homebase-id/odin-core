using System;
using Youverse.Core.Services.Drive;
using Youverse.Core.Services.Drives.FileSystem;

namespace Youverse.Core.Services.Drives.Base.Upload
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
    }
}