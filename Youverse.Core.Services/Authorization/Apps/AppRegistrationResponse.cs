using System;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationResponse
    {
        public Guid ApplicationId { get; set; }
        
        public string Name { get; set; }
        
        [Obsolete("is revoked comes from the EGR bound in the app")]
        public bool IsRevoked { get; set; }
        
        
        
    }
}