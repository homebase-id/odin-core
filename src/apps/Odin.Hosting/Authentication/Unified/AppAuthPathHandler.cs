#nullable enable
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Apps;
using Odin.Services.Base;

namespace Odin.Hosting.Authentication.Unified;

public static class AppAuthPathHandler
{
    public static async Task<AuthenticateResult> Handle(HttpContext context, IOdinContext odinContext, IdentityDatabase db)
    {
        if (!AuthUtils.TryGetClientAuthToken(context, YouAuthConstants.AppCookieName, out var authToken, true))
        {
            return AuthenticateResult.Fail("Invalid App Token");
        }

        var appRegService = context.RequestServices.GetRequiredService<IAppRegistrationService>();
        odinContext.SetAuthContext(YouAuthConstants.AppSchemeName);

        var ctx = await appRegService.GetAppPermissionContext(authToken, odinContext, db);

        if (null == ctx)
        {
            return AuthenticateResult.Fail("Invalid App Token");
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, odinContext.GetCallerOdinIdOrFail()), //caller is this owner
            new(OdinClaimTypes.IsAuthorizedApp, bool.TrueString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsIdentityOwner, bool.TrueString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsAuthorizedGuest, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer)
        };

        // Steal this path from the http controller because here we have the client auth token
        if (context.Request.Path.StartsWithSegments($"{AppApiPathConstants.NotificationsV1}/preauth"))
        {
            AuthenticationCookieUtil.SetCookie(context.Response, YouAuthConstants.AppCookieName, authToken);
        }

        return AuthUtils.CreateAuthenticationResult(claims, YouAuthConstants.AppSchemeName);
    }

    public static Task HandleSignOut(HttpContext context, IOdinContext odinContext, IdentityDatabase cn)
    {
        return Task.CompletedTask;
    }
}