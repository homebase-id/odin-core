using System;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Drive and file info which identifies a file to be used externally to the host. i.e. you can send this to the client
    /// </summary>
    public class ExternalFileIdentifier
    {
        /// <summary>
        /// The drive to access
        /// </summary>
        public TargetDrive TargetDrive { get; set; }
        
        /// <summary>
        /// The fileId to retrieve
        /// </summary>
        public Guid FileId { get; set; }
    }
}