#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

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

    public static AuthenticateResult CreateAuthenticationResult(List<Claim> claims, string scheme, ClientTokenType tokenType)
    {
        var claimsIdentity = new ClaimsIdentity(claims, scheme);
        AuthenticationProperties props = new AuthenticationProperties
        {
            Items =
            {
                [nameof(ClientTokenType)] = tokenType.ToString()
            }
        };

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), props, scheme);
        return AuthenticateResult.Success(ticket);
    }
}