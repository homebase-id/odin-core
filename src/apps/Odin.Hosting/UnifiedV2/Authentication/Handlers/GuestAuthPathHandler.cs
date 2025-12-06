#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Authorization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Membership.YouAuth;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public class GuestAuthPathHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken clientAuthToken, IOdinContext odinContext)
    {
        var youAuthRegService = context.RequestServices.GetRequiredService<YouAuthDomainRegistrationService>();
        var ctx = await youAuthRegService.GetDotYouContextAsync(clientAuthToken, odinContext);
        if (null == ctx)
        {
            return AuthHandlerResult.Fallback();
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);

        return AuthHandlerResult.Success();
    }

    public async Task HandleSignOutAsync(Guid tokenId, HttpContext context, IOdinContext odinContext)
    {
        await Task.CompletedTask;
    }
}