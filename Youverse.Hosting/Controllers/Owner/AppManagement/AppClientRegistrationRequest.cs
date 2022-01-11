using System;

namespace Youverse.Hosting.Controllers.Owner.AppManagement
{
    public class AppClientRegistrationRequest
    {
        public Guid ApplicationId { get; set; }
        public string ClientPublicKey64 { get; set; }
        
    }
}