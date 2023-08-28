using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.YouAuth;

namespace Odin.Hosting.Controllers.ClientToken;

public class AuthorizeValidExchangeGrant : AuthorizeAttribute
{
    public AuthorizeValidExchangeGrant()
    {
        AuthenticationSchemes = YouAuthConstants.YouAuthScheme;
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}