using System;
using Youverse.Core;

namespace Youverse.Hosting.Controllers.OwnerToken.AppManagement
{
    /// <summary>
    /// Data for registering a new client for a given app
    /// </summary>
    public class AppClientRegistrationRequest
    {
        /// <summary>
        /// The Id of the application
        /// </summary>
        public ByteArrayId AppId { get; set; }
        
        /// <summary>
        /// Base64 encoded RSA public key from the client.
        /// </summary>
        public string ClientPublicKey64 { get; set; }
        
        /// <summary>
        /// A user-readable name for the client.  This is usually the computer name, phone name, etc (i.e. Todd's Android).
        /// </summary>
        public string ClientFriendlyName { get; set; }
        
    }
}