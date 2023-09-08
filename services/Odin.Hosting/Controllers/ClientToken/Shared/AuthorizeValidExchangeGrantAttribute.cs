using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.ClientToken.Shared;

public class AuthorizeValidExchangeGrantAttribute : AuthorizeAttribute
{
    public AuthorizeValidExchangeGrantAttribute()
    {
        AuthenticationSchemes = YouAuthConstants.YouAuthScheme;
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}