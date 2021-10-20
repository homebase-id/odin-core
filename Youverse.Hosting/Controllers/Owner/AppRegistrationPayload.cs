using System;

namespace Youverse.Core.Services.Authorization.AppRegistration
{
    public class AppRegistrationPayload
    {
        public Guid ApplicationId { get; set; } 
        public string Name { get; set; }
    }
}