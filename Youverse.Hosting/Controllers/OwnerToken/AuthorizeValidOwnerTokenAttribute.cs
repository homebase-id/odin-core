using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.OwnerToken
{
    /// <summary>
    /// Requests must come from the owner logged into the console.  There is no app associated
    /// </summary>
    public class AuthorizeOwnerTokenAttribute : AuthorizeAttribute
    {
        public AuthorizeOwnerTokenAttribute()
        {
            AuthenticationSchemes = OwnerAuthConstants.SchemeName;
            Policy = OwnerPolicies.IsDigitalIdentityOwner;
        }
    }
}