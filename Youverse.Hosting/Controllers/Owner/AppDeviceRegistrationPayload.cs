using System;

namespace Youverse.Hosting.Controllers.Owner
{
    public class AppDeviceRegistrationPayload
    {
        public Guid ApplicationId { get; set; }
        public string DeviceId64 { get; set; }
        public string SharedSecret64 { get; set; }
    }
}