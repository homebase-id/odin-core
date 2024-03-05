﻿using System;
using Odin.Core.Services.Authorization.Acl;
using Odin.Core.Storage;

namespace Odin.Core.Services.Drives.DriveCore.Storage
{
    public class ServerMetadata
    {
        public AccessControlList AccessControlList { get; set; }
        
        /// <summary>
        /// If true, the file should not be indexed
        /// </summary>
        public bool DoNotIndex { get; set; }
        
        /// <summary>
        /// Indicates if this file can be distributed to Data Subscriptions
        /// </summary>
        public bool AllowDistribution { get; set; }
        
        /// <summary>
        /// Indicates the system type of file; this changes the internal behavior how the file is saved
        /// </summary>
        public FileSystemType FileSystemType { get; set; }

        public Int64 FileByteCount { get; set; }
    }
}