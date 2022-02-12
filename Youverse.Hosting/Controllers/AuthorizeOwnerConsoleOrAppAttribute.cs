using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers
{
    /// <summary>
    /// The request must come from the owner logged into the console or an app; The request must have an app id even if it is the owner console
    /// </summary>
    public class AuthorizeOwnerConsoleOrAppAttribute : AuthorizeAttribute
    {
        public AuthorizeOwnerConsoleOrAppAttribute()
        {
            AuthenticationSchemes = $"{OwnerAuthConstants.SchemeName},{AppAuthConstants.SchemeName}";
            Policy = OwnerPolicies.IsAuthorizedApp;
        }
    }
}