using System;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    public class AppRegistrationRequest
    {
        public Guid ApplicationId { get; set; } 
        public string Name { get; set; }
        public bool CreateDrive { get; set; }
        public bool CanManageConnections { get; set; }
        public Guid? DefaultDrivePublicId { get; set; }
    }
}