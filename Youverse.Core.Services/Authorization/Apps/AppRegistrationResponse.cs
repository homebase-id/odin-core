using System;

namespace Youverse.Core.Services.Authorization.Apps
{
    public class AppRegistrationResponse
    {
        public Guid AppId { get; set; }
        
        public string Name { get; set; }
        
        public bool IsRevoked { get; set; }
        
        public UInt64 Created { get; set; }

        public UInt64 Modified { get; set; }
    }
}