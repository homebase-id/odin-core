#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public class OwnerAuthPathHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
        var pushDeviceToken = PushNotificationCookieUtil.GetDeviceKey(context.Request);
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            ClientIdOrDomain = null,
            AccessRegistrationId = token.Id,
            DevicePushNotificationKey = pushDeviceToken
        };

        if (!await authService.UpdateOdinContextAsync(token, clientContext, odinContext))
        {
            return AuthHandlerResult.Fail();
        }

        if (odinContext.Caller.OdinId == null)
        {
            return AuthHandlerResult.Fail();
        }

        var claims = new List<Claim>()
        {
            new(ClaimTypes.Name, odinContext.Caller.OdinId, ClaimValueTypes.String, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsIdentityOwner, bool.TrueString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsAuthorizedApp, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsAuthorizedGuest, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer)
        };

        return new AuthHandlerResult
        {
            Status = AuthHandlerStatus.Success,
            Claims = claims
        };
    }

    public async Task HandleSignOutAsync(Guid tokenId, HttpContext context, IOdinContext odinContext)
    {
        var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
        await authService.ExpireTokenAsync(tokenId);
    }
}