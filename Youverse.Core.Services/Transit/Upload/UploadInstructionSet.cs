using System;
using Youverse.Core.Services.Drive.Storage;

namespace Youverse.Core.Services.Transit.Upload
{
    /// <summary>
    /// Specifies how an upload should be handled
    /// </summary>
    public class UploadInstructionSet
    {
        public StorageOptions StorageOptions { get; set; }
        
        public TransitOptions TransitOptions { get; set; }
    }
    
    /// <summary>
    /// Specifies what to do with a file when it is uploaded
    /// </summary>
    public class TransitOptions
    {
        public RecipientList Recipients { get; set; }
    }
    
    public class StorageOptions
    {
        /// <summary>
        /// The drive in which to store this file
        /// </summary>
        public Guid? DriveId { get; set; }
    
        /// <summary>
        /// Seconds in unix time UTC indicating when this file expires 
        /// </summary>
        public UInt64? ExpiresTimestamp { get; set; }
    }
}