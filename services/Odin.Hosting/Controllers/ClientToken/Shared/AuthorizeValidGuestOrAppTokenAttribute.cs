using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.ClientToken.Shared;

public class AuthorizeValidGuestOrAppTokenAttribute : AuthorizeAttribute
{
    public AuthorizeValidGuestOrAppTokenAttribute()
    {
        AuthenticationSchemes = YouAuthConstants.YouAuthScheme;
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}