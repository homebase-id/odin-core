using Microsoft.AspNetCore.Authentication;

namespace DotYou.TenantHost.Security.Authentication
{
    public class YFCookieAuthSchemeOptions : AuthenticationSchemeOptions
    {

        public string LoginUri { get; set; }
    }
}
