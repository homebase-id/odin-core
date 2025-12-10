#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Authorization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public class BuiltInBrowserAppHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        if (context.Request.Query.TryGetValue(GuestApiQueryConstants.IgnoreAuthCookie, out var values))
        {
            if (Boolean.TryParse(values.FirstOrDefault(), out var shouldIgnoreAuth))
            {
                if (shouldIgnoreAuth)
                {
                    return AuthHandlerResult.Fallback();
                }
            }
        }

        var homeAuthenticatorService = context.RequestServices.GetRequiredService<HomeAuthenticatorService>();
        var ctx = await homeAuthenticatorService.GetDotYouContextAsync(token, odinContext);

        if (ctx == null)
        {
            return AuthHandlerResult.Fallback();
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);
        return AuthHandlerResult.Success();
    }

    public Task HandleSignOutAsync(Guid tokenId, HttpContext context, IOdinContext odinContext)
    {
        return Task.CompletedTask;
    }
}