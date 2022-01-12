using System;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationResponse
    {
        public Guid ApplicationId { get; set; }
        
        public string Name { get; set; }

        public bool IsRevoked { get; set; }
        
        /// <summary>
        /// The drive associated with this app.
        /// </summary>
        public Guid? DriveId { get; set; }
    }
}