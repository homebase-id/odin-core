#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public static class OwnerAuthPathHandler
{
    public static async Task<AuthenticateResult> Handle(HttpContext context, ClientAuthenticationToken clientAuthToken, IOdinContext odinContext)
    {
        var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
        var pushDeviceToken = PushNotificationCookieUtil.GetDeviceKey(context.Request);
        var clientContext = new OdinClientContext
        {
            CorsHostName = null,
            ClientIdOrDomain = null,
            AccessRegistrationId = clientAuthToken.Id,
            DevicePushNotificationKey = pushDeviceToken
        };

        if (!await authService.UpdateOdinContextAsync(clientAuthToken, clientContext, odinContext))
        {
            return AuthenticateResult.Fail("Invalid Owner Token");
        }

        if (odinContext.Caller.OdinId == null)
        {
            return AuthenticateResult.Fail("Missing OdinId");
        }

        var claims = new List<Claim>()
        {
            new(ClaimTypes.Name, odinContext.Caller.OdinId, ClaimValueTypes.String, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsIdentityOwner, bool.TrueString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsAuthorizedApp, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsAuthorizedGuest, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer)
        };
       
        var result = AuthUtils.CreateAuthenticationResult(claims, 
            UnifiedAuthConstants.SchemeName,
            clientAuthToken);
        return result;
    }

    public static async Task HandleSignOut(HttpContext context, Guid tokenId)
    {
        var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
        await authService.ExpireTokenAsync(tokenId);
    }
}