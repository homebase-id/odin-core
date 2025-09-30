using System;

namespace Odin.Hosting.Controllers.OwnerToken.AppManagement
{
    public class AppClientRegistrationRequest
    {
        /// <summary>
        /// The ID of the application
        /// </summary>
        public Guid AppId { get; set; }
        
        public string JwkBase64UrlPublicKey { get; set; }
        
        /// <summary>
        /// A user-readable name for the client.  This is usually the computer name, phone name, etc (i.e. Todd's Android).
        /// </summary>
        public string ClientFriendlyName { get; set; }
        
    }
}