using System;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement
{
    public class AppClientRegistrationRequest
    {
        public Guid ApplicationId { get; set; }
        public string ClientPublicKey64 { get; set; }
        
    }
}