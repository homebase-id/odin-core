#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.YouAuth;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public static class GuestAuthPathHandler
{
    public static async Task<AuthenticateResult?> Handle(HttpContext context,
        ClientAuthenticationToken clientAuthToken,
        IOdinContext odinContext)
    {
        odinContext.SetAuthContext(YouAuthConstants.YouAuthScheme);

        var youAuthRegService = context.RequestServices.GetRequiredService<YouAuthDomainRegistrationService>();
        var ctx = await youAuthRegService.GetDotYouContextAsync(clientAuthToken, odinContext);
        if (null == ctx)
        {
            //if still no context, fall back to anonymous
            return null;
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);

        var result = AuthUtils.CreateAuthenticationResult(GetYouAuthClaims(odinContext),
            YouAuthConstants.YouAuthScheme,
            clientAuthToken);
        return result;
    }
    
    private static List<Claim> GetYouAuthClaims(IOdinContext odinContext)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, odinContext.GetCallerOdinIdOrFail()),
            new(OdinClaimTypes.IsIdentityOwner, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsAuthorizedApp, bool.FalseString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new(OdinClaimTypes.IsAuthorizedGuest, bool.TrueString, ClaimValueTypes.Boolean, OdinClaimTypes.Issuer)
        };
        return claims;
    }

    public static async Task HandleSignOut(HttpContext context, IOdinContext odinContext)
    {
        await Task.CompletedTask;
    }
}