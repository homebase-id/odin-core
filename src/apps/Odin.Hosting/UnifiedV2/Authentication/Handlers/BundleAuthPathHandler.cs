#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.Authorization.BundleTokens;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

/// <summary>
/// Authenticates a bundle token, acting as the app the request names, or as the token's primary app.
/// </summary>
public class BundleAuthPathHandler : IAuthPathHandler
{
    // A WebSocket upgrade cannot carry custom headers from a browser, so the acting app may also be named
    // as a Sec-WebSocket-Protocol value.
    private const string ActingAppProtocolPrefix = "odin.app.";

    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var ctx = await AuthenticateAsync(context, token, odinContext);
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

    /// <summary>The one place a request names its acting app: the header, else the WebSocket subprotocol.</summary>
    internal static Task<IOdinContext?> AuthenticateAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var actingAppId = context.Request.Headers[BundleTokenAuthenticator.ActingAppHeader].ToString();
        if (string.IsNullOrEmpty(actingAppId) && context.WebSockets.IsWebSocketRequest)
        {
            actingAppId = context.WebSockets.WebSocketRequestedProtocols
                .FirstOrDefault(p => p.StartsWith(ActingAppProtocolPrefix, StringComparison.Ordinal))
                ?.Substring(ActingAppProtocolPrefix.Length);
        }

        return context.RequestServices.GetRequiredService<BundleTokenAuthenticator>()
            .AuthenticateAsync(token, actingAppId, odinContext);
    }
}
