using Microsoft.AspNetCore.Authorization;
using Odin.Services.Authentication.Owner;
using Odin.Hosting.Authentication.Owner;

namespace Odin.Hosting.Controllers.OwnerToken
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