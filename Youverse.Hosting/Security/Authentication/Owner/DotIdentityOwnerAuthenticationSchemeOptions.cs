using Microsoft.AspNetCore.Authentication;

namespace Youverse.Hosting.Security.Authentication
{
    public class DotIdentityOwnerAuthenticationSchemeOptions : AuthenticationSchemeOptions
    {

        public string LoginUri { get; set; }
    }
}
