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
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public static class CdnAuthPathHandler
{
    static readonly List<string> AllowedPaths =
    [
        $"{UnifiedApiRouteConstants.Files}/payload",
        $"{UnifiedApiRouteConstants.Files}/thumb"
    ];

    public static async Task<AuthenticateResult> Handle(HttpContext context, ClientAuthenticationToken clientAuthToken,
        IOdinContext odinContext)
    {
        if (!IsValidPath(context))
        {
            return AuthenticateResult.Fail("Invalid path");
        }
        
        var config = context.RequestServices.GetRequiredService<OdinConfiguration>();

        if (config.Cdn.ExpectedAuthToken.Id != clientAuthToken.Id || 
            config.Cdn.ExpectedAuthToken.AccessTokenHalfKey != clientAuthToken.AccessTokenHalfKey)
        {
            return AuthenticateResult.Fail("Invalid auth token");
        }
        
        odinContext.SetAuthContext(YouAuthConstants.YouAuthScheme);

        return await CreateAuthResult(context, odinContext);
    }

    private static bool IsValidPath(HttpContext context)
    {
        var path = context.Request.Path;

        foreach (var allowed in AllowedPaths)
        {
            if (path.StartsWithSegments(allowed) ||
                path.StartsWithSegments(allowed + ".")) // the . is to handle thumbnail.{extension}
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<AuthenticateResult> CreateAuthResult(HttpContext httpContext, IOdinContext odinContext)
    {
        var driveManager = httpContext.RequestServices.GetRequiredService<IDriveManager>();
        var drives = await driveManager.GetCdnEnabledDrivesAsync(PageOptions.All, odinContext);

        if (!drives.Results.Any())
        {
           return AuthenticateResult.Fail("No CDN enabled drives configured");
        }

        var anonDriveGrants = drives.Results.Select(d => new DriveGrant()
        {
            DriveId = d.Id,
            PermissionedDrive = new PermissionedDrive()
            {
                Drive = d.TargetDriveInfo,
                Permission = DrivePermission.Read
            }
        }).ToList();

        var permissionGroupMap = new Dictionary<string, PermissionGroup>
        {
            { "read_cdn_drives", new PermissionGroup(new PermissionSet(), anonDriveGrants, null, null) },
        };

        odinContext.Caller = new CallerContext(
            odinId: null,
            securityLevel: SecurityGroupType.System,
            masterKey: null
        );

        odinContext.SetPermissionContext(
            new PermissionContext(
                permissionGroupMap,
                sharedSecretKey: null
            ));

        var claims = new[]
        {
            new Claim(OdinClaimTypes.IsIdentityOwner, bool.FalseString.ToLower(), ClaimValueTypes.Boolean,
                OdinClaimTypes.YouFoundationIssuer),
            new Claim(OdinClaimTypes.IsAuthenticated, bool.FalseString.ToLower(), ClaimValueTypes.Boolean,
                OdinClaimTypes.YouFoundationIssuer)
        };

        var claimsIdentity = new ClaimsIdentity(claims, YouAuthConstants.YouAuthScheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(claimsIdentity), YouAuthConstants.YouAuthScheme);
        return AuthenticateResult.Success(ticket);
    }
}