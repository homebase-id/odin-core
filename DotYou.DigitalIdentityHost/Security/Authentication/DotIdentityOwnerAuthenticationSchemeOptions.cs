using Microsoft.AspNetCore.Authentication;

namespace DotYou.TenantHost.Security.Authentication
{
    public class DotIdentityOwnerAuthenticationSchemeOptions : AuthenticationSchemeOptions
    {

        public string LoginUri { get; set; }
    }
}
