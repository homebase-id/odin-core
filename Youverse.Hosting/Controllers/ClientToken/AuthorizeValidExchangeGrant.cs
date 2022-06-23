using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.ClientToken;

namespace Youverse.Hosting.Controllers.ClientToken;

public class AuthorizeValidExchangeGrant : AuthorizeAttribute
{
    public AuthorizeValidExchangeGrant()
    {
        AuthenticationSchemes = ClientTokenConstants.Scheme;
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}