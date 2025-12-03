#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Odin.Core;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Configuration;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Hosting.UnifiedV2.Authentication.Handlers;

public class CdnAuthPathHandler : IAuthPathHandler
{
    static readonly List<string> AllowedPaths =
    [
        $"{UnifiedApiRouteConstants.Files}/payload",
        $"{UnifiedApiRouteConstants.Files}/thumb"
    ];

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

    private static async Task<AuthHandlerResult> CreateAuthResult(HttpContext httpContext, IOdinContext odinContext)
    {
        var driveManager = httpContext.RequestServices.GetRequiredService<IDriveManager>();
        var drives = await driveManager.GetCdnEnabledDrivesAsync(PageOptions.All, odinContext);

        if (!drives.Results.Any())
        {
            return AuthHandlerResult.Fail();
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
            masterKey: null,
            tokenType: ClientTokenType.Cdn
        );

        odinContext.SetPermissionContext(
            new PermissionContext(
                permissionGroupMap,
                sharedSecretKey: null
            ));

        return AuthHandlerResult.Success();
    }

    public async Task<AuthHandlerResult> HandleAsync(HttpContext context, ClientAuthenticationToken token, IOdinContext odinContext)
    {
        if (!IsValidPath(context))
        {
            return AuthHandlerResult.Fail();
        }

        var config = context.RequestServices.GetRequiredService<OdinConfiguration>();

        if (config.Cdn.Enabled == false)
        {
            return AuthHandlerResult.Fail();
        }

        var idMatches = config.Cdn.ExpectedAuthToken.Id == token.Id;
        var halfKeyMatches = ByteArrayUtil.EquiByteArrayCompare(
            config.Cdn.ExpectedAuthToken.AccessTokenHalfKey.GetKey(),
            token.AccessTokenHalfKey.GetKey());

        if (!(idMatches && halfKeyMatches))
        {
            return AuthHandlerResult.Fail();
        }

        return await CreateAuthResult(context, odinContext);
    }

    public async Task HandleSignOutAsync(Guid tokenId, HttpContext context, IOdinContext odinContext)
    {
        await Task.CompletedTask;
    }
}