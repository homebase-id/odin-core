#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Odin.Services.Authorization.ExchangeGrants;
using NotImplementedException = System.NotImplementedException;

namespace Odin.Hosting.Authentication.Unified;

public static class AuthUtils
{
    public static bool TryGetClientAuthToken(HttpContext context, string cookieName, out ClientAuthenticationToken clientAuthToken,
        bool preferHeader = false)
    {
        var clientAccessTokenValue64 = string.Empty;
        if (preferHeader)
        {
            clientAccessTokenValue64 = context.Request.Headers[cookieName];
        }

        if (string.IsNullOrWhiteSpace(clientAccessTokenValue64))
        {
            clientAccessTokenValue64 = context.Request.Cookies[cookieName];
        }

        return ClientAuthenticationToken.TryParse(clientAccessTokenValue64, out clientAuthToken);
    }

    public static AuthenticateResult CreateAuthenticationResult(List<Claim> claims, string scheme)
    {
        var claimsIdentity = new ClaimsIdentity(claims, scheme);
        // AuthenticationProperties authProperties = new AuthenticationProperties();
        // authProperties.IssuedUtc = DateTime.UtcNow;
        // authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
        // authProperties.AllowRefresh = true;
        // authProperties.IsPersistent = true;

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), scheme);
        return AuthenticateResult.Success(ticket);
    }
}