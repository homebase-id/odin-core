using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.Owner;

namespace Youverse.Hosting.Controllers.Owner
{
    public class OwnerAppAuthorizeAttribute : AuthorizeAttribute
    {
        public OwnerAppAuthorizeAttribute()
        {
            AuthenticationSchemes = $"{OwnerAuthConstants.SchemeName},{AppAuthConstants.SchemeName}";
            Policy = OwnerPolicies.IsAuthorizedApp;
        }
    }
}