using System;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationResponse
    {
        public Guid ApplicationId { get; set; }
        
        public string Name { get; set; }

        [Obsolete]
        public bool IsRevoked { get; set; }
        
    }
}