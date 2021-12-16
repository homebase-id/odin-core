using System;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Information about a container
    /// </summary>
    public class DriveInfo
    {
        public Guid DriveId { get; init; }
        
        public string IndexName { get; init; }
    }
}