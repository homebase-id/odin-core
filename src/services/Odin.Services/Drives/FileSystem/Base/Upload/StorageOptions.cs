using System;

namespace Odin.Services.Drives.FileSystem.Base.Upload
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

        // field to allow v2 set the drive id w/o being bound to the TargetDrive type
        public Guid DriveId { get; set; }

        /// <summary>
        /// The fileId to overwrite if it exists
        /// </summary>
        public Guid? OverwriteFileId { get; set; }

        public StorageIntent StorageIntent { get; set; } = StorageIntent.NewFileOrOverwrite;

        // public bool IgnoreMissingReferencedFile { get; set; }
    }

    /// <summary>
    /// Indicates how the uploaded file should be handled when saving or reconciling the system
    /// </summary>
    public enum StorageIntent
    {
        /// <summary>
        /// Stores the data as uploaded; meaning if you overwrite a file that has a payload; you must also provide
        /// the payload in the upload, otherwise the existing payload will be deleted (same applies to thumbnails)
        /// </summary>
        NewFileOrOverwrite = 0,

        /// <summary>
        /// Updates metadata without touching the thumbnails or payload; however the metadata
        /// thumbnails and payload collections will be validated to match the existing metadata file
        /// </summary>
        MetadataOnly = 2
    }
}