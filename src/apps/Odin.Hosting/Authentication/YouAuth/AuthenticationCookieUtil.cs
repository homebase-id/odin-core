using System;
using Microsoft.AspNetCore.Http;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Authentication.YouAuth;

internal static class AuthenticationCookieUtil
{
    public static void SetCookie(HttpResponse response, string cookieName, ClientAuthenticationToken authToken, SameSiteMode ssm = SameSiteMode.Strict, bool partioned = false)
    {
        var options = new CookieOptions()
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = true,
            SameSite = ssm,
            Expires = DateTime.UtcNow.AddMonths(6),
            Path = partioned ? "/; Partitioned" : null
        };
        response.Cookies.Append(cookieName, authToken.ToString(), options);
    }
    
}