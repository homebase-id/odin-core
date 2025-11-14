#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;
using Odin.Services.Base;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public static class OwnerAuthPathHandler
{
    public static async Task<AuthenticateResult> Handle(HttpContext context, IOdinContext odinContext)
    {
        if (AuthUtils.TryGetClientAuthToken(context, OwnerAuthConstants.CookieName, out var authResult))
        {
            var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
            var pushDeviceToken = PushNotificationCookieUtil.GetDeviceKey(context.Request);
            var clientContext = new OdinClientContext
            {
                CorsHostName = null,
                ClientIdOrDomain = null,
                AccessRegistrationId = authResult.Id,
                DevicePushNotificationKey = pushDeviceToken
            };

            if (!await authService.UpdateOdinContextAsync(authResult, clientContext, odinContext))
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

            var identity = new ClaimsIdentity(claims, OwnerAuthConstants.SchemeName);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);

            AuthenticationProperties authProperties = new AuthenticationProperties
            {
                IssuedUtc = DateTime.UtcNow,
                ExpiresUtc = DateTime.UtcNow.AddDays(1),
                AllowRefresh = true,
                IsPersistent = true
            };

            var ticket = new AuthenticationTicket(principal, authProperties, UnifiedAuthConstants.SchemeName);
            ticket.Properties.SetParameter(OwnerAuthConstants.CookieName, authResult.Id);
            return AuthenticateResult.Success(ticket);
        }

        return AuthenticateResult.Fail("Invalid or missing token");
    }

    public static async Task HandleSignOut(HttpContext context)
    {
        if (AuthUtils.TryGetClientAuthToken(context, OwnerAuthConstants.CookieName, out var token))
        {
            var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
            await authService.ExpireTokenAsync(token.Id);
        }
    }
}