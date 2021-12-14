using Microsoft.AspNetCore.Authentication;

namespace Youverse.Hosting.Authentication.Owner
{
    public class DotIdentityOwnerAuthenticationSchemeOptions : AuthenticationSchemeOptions
    {

        public string LoginUri { get; set; }
    }
}
