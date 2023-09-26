using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.ClientToken.App
{
    /// <summary>
    /// The request must come from the owner logged into an app
    /// </summary>
    public class AuthorizeValidAppTokenAttribute : AuthorizeAttribute
    {
        public AuthorizeValidAppTokenAttribute()
        {
            AuthenticationSchemes = YouAuthConstants.YouAuthScheme;
            Policy = YouAuthPolicies.IsAuthorizedApp;
        }
    }
}