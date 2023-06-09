using Microsoft.AspNetCore.Authorization;
using Odin.Hosting.Authentication.ClientToken;

namespace Odin.Hosting.Controllers.ClientToken;

public class AuthorizeValidExchangeGrant : AuthorizeAttribute
{
    public AuthorizeValidExchangeGrant()
    {
        AuthenticationSchemes = ClientTokenConstants.YouAuthScheme;
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}