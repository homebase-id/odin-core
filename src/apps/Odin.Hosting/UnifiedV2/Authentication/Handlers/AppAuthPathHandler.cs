#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.App;
using Odin.Services.Authorization.Apps;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public class AppAuthPathHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var appRegService = context.RequestServices.GetRequiredService<IAppRegistrationService>();

        var ctx = await appRegService.GetAppPermissionContextAsync(token, odinContext);

        if (null == ctx)
        {
            return AuthHandlerResult.Fail();
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);

        // Steal this path from the http controller because here we have the client auth token
        if (context.Request.Path.StartsWithSegments($"{AppApiPathConstantsV1.NotificationsV1}/preauth"))
        {
            AuthenticationCookieUtil.SetCookie(context.Response, OdinHeaderNames.AppCookie, token);
        }

        return AuthHandlerResult.Success();
    }

    public Task HandleSignOutAsync(Guid tokenId, HttpContext context, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}