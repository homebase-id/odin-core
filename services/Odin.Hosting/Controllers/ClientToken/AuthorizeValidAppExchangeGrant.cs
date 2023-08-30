using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.ClientToken
{
    /// <summary>
    /// The request must come from the owner logged into an app
    /// </summary>
    public class AuthorizeValidAppExchangeGrant : AuthorizeAttribute
    {
        public AuthorizeValidAppExchangeGrant()
        {
            AuthenticationSchemes = YouAuthConstants.YouAuthScheme;
            Policy = YouAuthPolicies.IsAuthorizedApp;
        }
    }
}