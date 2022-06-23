using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;
using Youverse.Hosting.Authentication.YouAuth;

namespace Youverse.Hosting.Controllers
{
    /// <summary>
    /// The request must come from the owner logged into the console or an app; The request must have an app id even if it is the owner console
    /// </summary>
    public class AuthorizeValidAppExchangeGrant : AuthorizeAttribute
    {
        public AuthorizeValidAppExchangeGrant()
        {
            AuthenticationSchemes = $"{AppAuthConstants.SchemeName}";
            Policy = OwnerPolicies.IsAuthorizedApp;
        }
    }

    public class AuthorizeValidExchangeGrant : AuthorizeAttribute
    {
        public AuthorizeValidExchangeGrant()
        {
            AuthenticationSchemes = $"{AppAuthConstants.SchemeName},{YouAuthConstants.Scheme}";
            // Policy = OwnerPolicies.IsAuthorizedApp;
        }
    }
}