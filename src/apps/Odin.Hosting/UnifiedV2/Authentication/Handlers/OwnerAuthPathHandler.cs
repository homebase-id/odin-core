#nullable enable
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core.Identity;
using Odin.Services.AppNotifications.Push;
using Odin.Services.Authentication.Owner;
using Odin.Services.Authorization;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Base;
using Odin.Services.Configuration.VersionUpgrade;
using Odin.Services.Security.PasswordRecovery.Shamir;
using Odin.Services.Tenant;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public class OwnerAuthPathHandler : IAuthPathHandler
{
    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        // Read below, and this runs ahead of OdinContextMiddleware
        odinContext.Tenant = (OdinId)context.RequestServices.GetRequiredService<ITenantProvider>().GetCurrentTenant()?.Name;

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

        // What V1's OwnerAuthenticationHandler does on every owner request. An upgrade is scheduled by the owner's own
        // requests, and the security page counts on a dead friend deal being gone by the time it reads, so once the
        // console's calls are here these can't be left to V1.
        await context.RequestServices.GetRequiredService<VersionUpgradeScheduler>().EnsureScheduledAsync(token, odinContext);
        await context.RequestServices.GetRequiredService<ShamirConfigurationService>().RemoveDeadFriendDealIfNeeded(odinContext);

        if (odinContext.Caller.OdinId == null)
        {
            return AuthHandlerResult.Fail();
        }

        var claims = new List<Claim>();
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