using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.OwnerToken
{
    /// <summary>
    /// Requests must come from the owner logged into the console.  There is no app associated
    /// </summary>
    public class AuthorizeValidOwnerTokenAttribute : AuthorizeAttribute
    {
        public AuthorizeValidOwnerTokenAttribute()
        {
            AuthenticationSchemes = OwnerAuthConstants.SchemeName;
            Policy = OwnerPolicies.IsDigitalIdentityOwner;
        }
    }
}