using System;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Drive and file info which identifies a file to be used externally to the host. i.e. you can send this to the client
    /// </summary>
    public struct ExternalFileIdentifier
    {
        public TargetDrive TargetDrive { get; set; }
        public Guid FileId { get; set; }
    }
}