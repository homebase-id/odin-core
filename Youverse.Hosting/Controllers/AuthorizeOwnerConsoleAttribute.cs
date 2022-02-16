using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers
{
    /// <summary>
    /// Requests must come from the owner logged into the console.  There is no app associated
    /// </summary>
    public class AuthorizeOwnerConsoleAttribute : AuthorizeAttribute
    {
        public AuthorizeOwnerConsoleAttribute()
        {
            AuthenticationSchemes = $"{OwnerAuthConstants.SchemeName}";
            Policy = OwnerPolicies.IsDigitalIdentityOwner;
        }
    }
}