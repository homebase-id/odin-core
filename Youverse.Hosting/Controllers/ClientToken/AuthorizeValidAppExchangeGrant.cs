using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.ClientToken;

namespace Youverse.Hosting.Controllers.ClientToken
{
    /// <summary>
    /// The request must come from the owner logged into an app
    /// </summary>
    public class AuthorizeValidAppExchangeGrant : AuthorizeAttribute
    {
        public AuthorizeValidAppExchangeGrant()
        {
            AuthenticationSchemes = ClientTokenConstants.Scheme;
            Policy = ClientTokenPolicies.IsAuthorizedApp;
        }
    }
}