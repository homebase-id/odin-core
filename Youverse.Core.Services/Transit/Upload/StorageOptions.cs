﻿using System;

namespace Youverse.Core.Services.Transit.Upload
{
    public class StorageOptions
    {
        /// <summary>
        /// The drive in which to store this file
        /// </summary>
        public Guid? DriveId { get; set; }
    
        /// <summary>
        /// The fileId to overwrite if it exists
        /// </summary>
        public Guid? OverwriteFileId { get; set; }
        
        /// <summary>
        /// Seconds in unix time UTC indicating when this file expires 
        /// </summary>
        public UInt64? ExpiresTimestamp { get; set; }
    }
}