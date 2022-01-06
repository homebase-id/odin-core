using System;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    public class AppDeviceRegistrationRequest
    {
        public Guid ApplicationId { get; set; }
        public string DeviceId64 { get; set; }
        public string SharedSecretKey64 { get; set; }
        
    }
}