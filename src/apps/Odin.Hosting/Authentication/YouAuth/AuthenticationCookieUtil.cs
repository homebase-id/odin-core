using System;
using Microsoft.AspNetCore.Http;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Authentication.YouAuth;

internal static class AuthenticationCookieUtil
{
    public static void SetCookie(HttpResponse response, string cookieName, ClientAuthenticationToken authToken,
        SameSiteMode ssm = SameSiteMode.Strict)
    {
        var options = new CookieOptions()
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = true,
            SameSite = ssm,
            Expires = DateTime.UtcNow.AddMonths(6),
        };

        // SameSite=None cookies are only sent cross-site when partitioned (CHIPS); required so
        // apps hosted on a different site than the identity can carry the cookie on the WS upgrade.
        if (ssm == SameSiteMode.None)
        {
            options.Extensions.Add("Partitioned");
        }

        response.Cookies.Append(cookieName, authToken.ToString(), options);
    }
}