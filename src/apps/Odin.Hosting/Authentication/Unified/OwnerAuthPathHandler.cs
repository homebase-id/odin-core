#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;
using Odin.Services.Base;

namespace Odin.Hosting.Authentication.Unified;

public static class OwnerAuthPathHandler
{
    public static async Task<AuthenticateResult> Handle(HttpContext context, IOdinContext odinContext, IdentityDatabase cn)
    {
        if (AuthUtils.TryGetClientAuthToken(context, OwnerAuthConstants.CookieName, out var authResult))
        {
            try
            {
                var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
                if (!await authService.UpdateOdinContext(authResult, odinContext, cn))
                {
                    return AuthenticateResult.Fail("Invalid Owner Token");
                }
            }
            catch (OdinSecurityException e)
            {
                return AuthenticateResult.Fail(e.Message);
            }

            if (odinContext.Caller.OdinId == null)
            {
                return AuthenticateResult.Fail("Missing OdinId");
            }

            var claims = new List<Claim>()
            {
                new(ClaimTypes.Name, odinContext.Caller.OdinId, ClaimValueTypes.String, OdinClaimTypes.Issuer),
                new(OdinClaimTypes.IsAuthenticated, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
                new(OdinClaimTypes.IsIdentityOwner, bool.TrueString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
                new(OdinClaimTypes.IsAuthorizedGuest, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.Issuer)
            };

            var identity = new ClaimsIdentity(claims, OwnerAuthConstants.SchemeName);
            ClaimsPrincipal principal = new ClaimsPrincipal(identity);

            AuthenticationProperties authProperties = new AuthenticationProperties();
            authProperties.IssuedUtc = DateTime.UtcNow;
            authProperties.ExpiresUtc = DateTime.UtcNow.AddDays(1);
            authProperties.AllowRefresh = true;
            authProperties.IsPersistent = true;

            var ticket = new AuthenticationTicket(principal, authProperties, UnifiedAuthConstants.SchemeName);
            ticket.Properties.SetParameter(OwnerAuthConstants.CookieName, authResult.Id);
            return AuthenticateResult.Success(ticket);
        }

        return AuthenticateResult.Fail("Invalid or missing token");
    }

    public static Task HandleSignOut(HttpContext context, object odinContext, IdentityDatabase cn)
    {
        if (AuthUtils.TryGetClientAuthToken(context, OwnerAuthConstants.CookieName, out var authResult))
        {
            var authService = context.RequestServices.GetRequiredService<OwnerAuthenticationService>();
            authService.ExpireToken(authResult.Id, cn);
        }

        return Task.CompletedTask;
    }
}