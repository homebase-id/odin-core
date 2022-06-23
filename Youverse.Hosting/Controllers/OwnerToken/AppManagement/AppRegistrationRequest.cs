using System;
using Youverse.Core.Services.Authorization.Permissions;
using Youverse.Core.Services.Drive;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement
{
    public class AppRegistrationRequest
    {
        public Guid ApplicationId { get; set; } 
        
        public string Name { get; set; }
        
        public bool CreateDrive { get; set; }

        public PermissionSet PermissionSet { get; set; }
        
        public TargetDrive TargetDrive { get; set; }
        
        public string DriveMetadata { get; set; }
        
        public bool DriveAllowAnonymousReads { get; set; }
        
        public string DriveName { get; set; }
    }
}