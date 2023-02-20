using Youverse.Core.Services.Authorization.Acl;
using Youverse.Core.Services.Drives.FileSystem;

namespace Youverse.Core.Services.Drive.Core.Storage
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
    }
}