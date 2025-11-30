#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public static class AuthUtils
{
    public static AuthenticateResult CreateAuthenticationResult(List<Claim> claims, string scheme, ClientAuthenticationToken token)
    {
        var claimsIdentity = new ClaimsIdentity(claims, scheme);
        AuthenticationProperties props = new AuthenticationProperties();
        props.SetParameter("id", token.Id);
        props.SetParameter("type", token.ClientTokenType.ToString());
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), props, scheme);
        return AuthenticateResult.Success(ticket);
    }
}