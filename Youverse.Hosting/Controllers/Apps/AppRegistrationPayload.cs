using System;

namespace Youverse.Hosting.Controllers.Apps
{
    public class AppRegistrationPayload
    {
        public Guid ApplicationId { get; set; } 
        public string Name { get; set; }
    }
}