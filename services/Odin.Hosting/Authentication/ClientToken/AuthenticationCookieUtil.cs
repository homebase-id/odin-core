using System;
using Microsoft.AspNetCore.Http;
using Odin.Core.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Authentication.ClientToken;

internal static class AuthenticationCookieUtil
{
    public static void SetCookie(HttpResponse response, string cookieName, ClientAuthenticationToken authToken)
    {
        SetCookie(response,cookieName, authToken, null);
    }

    public static void SetCookie(HttpResponse response, string cookieName, ClientAuthenticationToken authToken, string domain)
    {
        var options = new CookieOptions()
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = true,
            //Path = "/owner", //TODO: cannot use this until we adjust api paths
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddMonths(6),
            Domain = string.IsNullOrEmpty(domain) ? null : $".${domain}"
        };

        response.Cookies.Append(cookieName, authToken.ToString(), options);
    }
}
