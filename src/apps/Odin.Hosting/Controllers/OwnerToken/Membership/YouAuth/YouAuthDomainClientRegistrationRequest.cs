namespace Odin.Hosting.Controllers.OwnerToken.Membership.YouAuth
{
    /// <summary>
    /// Data for registering a new client for a given app
    /// </summary>
    public class YouAuthDomainClientRegistrationRequest
    {
        /// <summary>
        /// The Id of the application
        /// </summary>
        public string Domain { get; set; }
        
        /// <summary>
        /// A user-readable name for the client.  This is usually the computer name, phone name, etc (i.e. Todd's Android).
        /// </summary>
        public string ClientFriendlyName { get; set; }
        
    }
}