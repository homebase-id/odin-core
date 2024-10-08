using System;
using Microsoft.AspNetCore.Http;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Authentication.YouAuth;

internal static class AuthenticationCookieUtil
{
    public static void SetCookie(HttpResponse response, string cookieName, ClientAuthenticationToken authToken)
    {
        var options = new CookieOptions()
        {
            HttpOnly = true,
            IsEssential = true,
            Secure = true,
            //Path = "/owner", //TODO: cannot use this until we adjust api paths
            SameSite = SameSiteMode.Strict,
            Expires = DateTime.UtcNow.AddMonths(6)
        };
            
        response.Cookies.Append(cookieName, authToken.ToString(), options);
    }
    
}