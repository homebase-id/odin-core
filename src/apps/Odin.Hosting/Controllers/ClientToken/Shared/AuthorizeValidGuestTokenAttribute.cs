using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.ClientToken.Shared;

public class AuthorizeValidGuestTokenAttribute : AuthorizeAttribute
{
    public AuthorizeValidGuestTokenAttribute()
    {
        AuthenticationSchemes = YouAuthConstants.YouAuthScheme;
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}