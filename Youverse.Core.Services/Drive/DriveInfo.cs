using System;

namespace Youverse.Core.Services.Drive
{
    /// <summary>
    /// Information about a container
    /// </summary>
    public class DriveInfo
    {
        public Guid Id { get; init; }
        
        public string RootPath { get; init; }
        public string IndexName { get; init; }

        public string PermissionIndexName { get; init; }
        
        public string IndexPath { get; init; }
        
        public string PermissionIndexPath { get; init; }
    }
}