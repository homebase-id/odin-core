using System;

namespace Youverse.Core.Services.Authentication
{
    /// <summary>
    /// Holds the tokens required when a device has been authenticated.  These should be
    /// different than the Authentication token
    /// </summary>
    public class DeviceAuthenticationResult
    {
        //TODO determine this during my next meetup with Michael
        public Guid DeviceToken { get; set; }
        
        public DotYouAuthenticationResult AuthenticationResult { get; set; }
        
    }
}