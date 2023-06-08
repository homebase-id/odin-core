using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.ClientToken;

namespace Odin.Hosting.Controllers.ClientToken
{
    /// <summary>
    /// The request must come from the owner logged into an app
    /// </summary>
    public class AuthorizeValidAppExchangeGrant : AuthorizeAttribute
    {
        public AuthorizeValidAppExchangeGrant()
        {
            AuthenticationSchemes = ClientTokenConstants.YouAuthScheme;
            Policy = ClientTokenPolicies.IsAuthorizedApp;
        }
    }
}