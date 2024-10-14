#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core;
using Odin.Core.Exceptions;
using Odin.Core.Storage.SQLite.IdentityDatabase;
using Odin.Hosting.Authentication.YouAuth;
using Odin.Hosting.Controllers.ClientToken.Guest;
using Odin.Hosting.Controllers.Home.Service;
using Odin.Services.Authentication.YouAuth;
using Odin.Services.Authorization;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;
using Odin.Services.Membership.YouAuth;

namespace Odin.Hosting.Authentication.Unified;

public static class GuestAuthPathHandler
{
    public static async Task<AuthenticateResult> Handle(HttpContext context, IOdinContext odinContext, IdentityDatabase cn)
    {
        odinContext.SetAuthContext(YouAuthConstants.YouAuthScheme);

        if (!AuthUtils.TryGetClientAuthToken(context, YouAuthDefaults.XTokenCookieName, out var clientAuthToken))
        {
            return AuthenticateResult.Success(await CreateAnonYouAuthTicket(context, odinContext, cn));
        }

        if (clientAuthToken.ClientTokenType == ClientTokenType.BuiltInBrowserApp)
        {
            return await HandleBuiltInBrowserAppToken(context, clientAuthToken, odinContext, cn);
        }

        if (clientAuthToken.ClientTokenType == ClientTokenType.YouAuth)
        {
            return await HandleYouAuthToken(context, clientAuthToken, odinContext, cn);
        }

        throw new OdinClientException("Unhandled YouAuth token type");
    }

    private static async Task<AuthenticateResult> HandleBuiltInBrowserAppToken(HttpContext context,
        ClientAuthenticationToken clientAuthToken,
        IOdinContext odinContext, IdentityDatabase cn)
    {
        if (context.Request.Query.TryGetValue(GuestApiQueryConstants.IgnoreAuthCookie, out var values))
        {
            if (Boolean.TryParse(values.FirstOrDefault(), out var shouldIgnoreAuth))
            {
                if (shouldIgnoreAuth)
                {
                    return AuthenticateResult.Success(await CreateAnonYouAuthTicket(context, odinContext, cn));
                }
            }
        }

        var homeAuthenticatorService = context.RequestServices.GetRequiredService<HomeAuthenticatorService>();
        var ctx = await homeAuthenticatorService.GetDotYouContext(clientAuthToken, odinContext, cn);

        if (null == ctx)
        {
            //if still no context, fall back to anonymous
            return AuthenticateResult.Success(await CreateAnonYouAuthTicket(context, odinContext, cn));
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);
        return AuthUtils.CreateAuthenticationResult(GetYouAuthClaims(odinContext), YouAuthConstants.YouAuthScheme);
    }

    private static async Task<AuthenticateResult> HandleYouAuthToken(HttpContext context, ClientAuthenticationToken clientAuthToken,
        IOdinContext odinContext,
        IdentityDatabase cn)
    {
        var youAuthRegService = context.RequestServices.GetRequiredService<YouAuthDomainRegistrationService>();
        var ctx = await youAuthRegService.GetDotYouContext(clientAuthToken, odinContext, cn);
        if (null == ctx)
        {
            //if still no context, fall back to anonymous
            return AuthenticateResult.Success(await CreateAnonYouAuthTicket(context, odinContext, cn));
        }

        odinContext.Caller = ctx.Caller;
        odinContext.SetPermissionContext(ctx.PermissionsContext);

        return AuthUtils.CreateAuthenticationResult(GetYouAuthClaims(odinContext), YouAuthConstants.YouAuthScheme);
    }


    private static async Task<AuthenticationTicket> CreateAnonYouAuthTicket(HttpContext context, IOdinContext odinContext,
        IdentityDatabase cn)
    {
        var driveManager = context.RequestServices.GetRequiredService<DriveManager>();
        var anonymousDrives = await driveManager.GetAnonymousDrives(PageOptions.All, odinContext, cn);

        if (!anonymousDrives.Results.Any())
        {
            throw new OdinClientException(
                "No anonymous drives configured.  There should be at least one; be sure you accessed /owner to initialize them.",
                OdinClientErrorCode.NotInitialized);
        }

        var anonDriveGrants = anonymousDrives.Results.Select(d => new DriveGrant()
        {
            DriveId = d.Id,
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = d.TargetDriveInfo,
                Permission = DrivePermission.Read
            }
        }).ToList();

        var tenantContext = context.RequestServices.GetRequiredService<TenantContext>();
        var anonPerms = new List<int>();

        if (tenantContext.Settings.AnonymousVisitorsCanViewConnections)
        {
            anonPerms.Add(PermissionKeys.ReadConnections);
        }

        if (tenantContext.Settings.AnonymousVisitorsCanViewWhoIFollow)
        {
            anonPerms.Add(PermissionKeys.ReadWhoIFollow);
        }

        var permissionGroupMap = new Dictionary<string, PermissionGroup>
        {
            { "read_anonymous_drives", new PermissionGroup(new PermissionSet(anonPerms), anonDriveGrants, null, null) },
        };

        odinContext.Caller = new CallerContext(
            odinId: null,
            securityLevel: SecurityGroupType.Anonymous,
            masterKey: null
        );

        odinContext.SetPermissionContext(
            new PermissionContext(
                permissionGroupMap,
                sharedSecretKey: null
            ));

        var claims = new[]
        {
            new Claim(OdinClaimTypes.IsIdentityOwner, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.Issuer),
            new Claim(OdinClaimTypes.IsAuthenticated, bool.FalseString.ToLower(), ClaimValueTypes.Boolean, OdinClaimTypes.Issuer)
        };

        var claimsIdentity = new ClaimsIdentity(claims, YouAuthConstants.YouAuthScheme);
        return new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), YouAuthConstants.YouAuthScheme);
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

    public static Task HandleSignOut(HttpContext context, IOdinContext odinContext, IdentityDatabase cn)
    {
        return Task.CompletedTask;
    }
}