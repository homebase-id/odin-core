#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Authorization.BundleTokens;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

/// <summary>
/// Authenticates a bundle token.  The acting app comes from the <c>X-ODIN-APP-ID</c> header and must be
/// one the token reaches; without the header the token acts as its primary app.
/// </summary>
public class BundleAuthPathHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var authenticator = context.RequestServices.GetRequiredService<BundleTokenAuthenticator>();
        var actingAppId = context.Request.Headers[BundleTokenAuthenticator.ActingAppHeader].ToString();

        var ctx = await authenticator.AuthenticateAsync(token, actingAppId, odinContext);
        if (null == ctx)
        {
            return AuthHandlerResult.Fail();
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
