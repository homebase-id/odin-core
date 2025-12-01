using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.UnifiedV2.Authentication;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization.ExchangeGrants;

namespace Odin.Hosting.Middleware.V2Auth;

public static class UnifiedCookieMigrationMiddlewareExtensions
{
    public static IApplicationBuilder UseCookieMigration(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UnifiedCookieMigrationMiddleware>();
    }
}

public class UnifiedCookieMigrationMiddleware(RequestDelegate next)
{
    private static readonly string[] LegacyCookieNames =
    {
        YouAuthConstants.AppCookieName,
        OwnerAuthConstants.CookieName,
        YouAuthDefaults.XTokenCookieName
    };
    
    public async Task InvokeAsync(HttpContext context)
    {
        // If user already has the unified cookie, do nothing
        if (!context.Request.Cookies.ContainsKey(UnifiedAuthConstants.CookieName))
        {
            foreach (var legacyName in LegacyCookieNames)
            {
                if (context.Request.Cookies.TryGetValue(legacyName, out var tokenValue) &&
                    !string.IsNullOrWhiteSpace(tokenValue) && ClientAuthenticationToken.TryParse(tokenValue, out var authToken))
                {
                    // Write a *new* cookie without modifying the original
                    AuthenticationCookieUtil.SetCookie(
                        context.Response,
                        UnifiedAuthConstants.CookieName,
                        authToken
                    );

                    break; // stop after the first valid legacy cookie
                }
            }
        }

        await next(context);
    }
}