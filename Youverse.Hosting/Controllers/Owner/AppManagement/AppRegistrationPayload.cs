using System;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    public class AppRegistrationPayload
    {
        public Guid ApplicationId { get; set; } 
        public string Name { get; set; }
    }
}