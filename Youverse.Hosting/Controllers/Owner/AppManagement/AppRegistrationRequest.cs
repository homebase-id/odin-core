using System;
using Youverse.Core.Services.Authorization.Permissions;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    public class AppRegistrationRequest
    {
        public Guid ApplicationId { get; set; } 
        
        public string Name { get; set; }
        
        public bool CreateDrive { get; set; }

        public PermissionSet PermissionSet { get; set; }
        
        public Guid DefaultDrivePublicId { get; set; }
        public string DriveMetadata { get; set; }
        
        public bool DriveAllowAnonymousReads { get; set; }
        public Guid DriveType { get; set; }
        public string DriveName { get; set; }
    }
}