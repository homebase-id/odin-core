using System;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// Specifies what to do with a file when it is uploaded
    /// </summary>
    public class TransitOptions
    {
        public RecipientList Recipients { get; set; }

        public StorageOptions StorageOptions { get; set; }
    }
    
    public class StorageOptions
    {
        /// <summary>
        /// The drive in which to store this file
        /// </summary>
        private Guid? DriveId { get; set; }

        /// <summary>
        /// If true, the file will be kept in the <see cref="DriveId"/>; otherwise it will be deleted after being sent
        /// </summary>
        private bool StoreFileLongTerm { get; set; }
    }
}