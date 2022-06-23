using Microsoft.AspNetCore.Authorization;
using Youverse.Hosting.Authentication.App;
using Youverse.Hosting.Authentication.YouAuth;

namespace Youverse.Hosting.Controllers;

public class AuthorizeValidExchangeGrant : AuthorizeAttribute
{
    public AuthorizeValidExchangeGrant()
    {
        AuthenticationSchemes = $"{AppAuthConstants.SchemeName},{YouAuthConstants.Scheme}";
        // Policy = OwnerPolicies.IsAuthorizedApp;
    }
}