using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authorization.Acl;
using Odin.Services.Authorization.ExchangeGrants;
using Odin.Services.Authorization.Permissions;
using Odin.Services.Base;
using Odin.Services.Drives;
using Odin.Services.Drives.Management;

namespace Odin.Services.LinkPreview;

public class LinkPreviewAuthenticationService(DriveManager driveManager)
{
    // private readonly SharedOdinContextCache<LinkPreviewAuthenticationService> _cache;

    /// <summary>
    /// Gets the <see cref="IOdinContext"/> for the specified token from cache or disk.
    /// </summary>
    public async Task<IOdinContext> GetDotYouContextAsync(IOdinContext odinContext)
    {
        var creator = new Func<Task<IOdinContext>>(async () =>
        {
            var dotYouContext = new OdinContext();
            var (callerContext, permissionContext) = await GetPermissionContextAsync(odinContext);
        
            if (null == permissionContext || callerContext == null)
            {
                return null;
            }
        
            dotYouContext.Caller = callerContext;
            dotYouContext.SetPermissionContext(permissionContext);
        
            return dotYouContext;
        });
        
        return await creator();

        //TODO: cache this but we need a token
        // return await _cache.GetOrAddContextAsync(token, creator);
    }

    private async Task<(CallerContext callerContext, PermissionContext permissionContext)> GetPermissionContextAsync(
        IOdinContext odinContext)
    {
        var anonymousDrives = await driveManager.GetAnonymousDrivesAsync(PageOptions.All, odinContext);
        if (!anonymousDrives.Results.Any())
        {
            return (null, null);
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

        var permissionGroupMap = new Dictionary<string, PermissionGroup>
        {
            { "read_anonymous_drives", new PermissionGroup(new PermissionSet(), anonDriveGrants, null, null) },
        };

        var callerContext = new CallerContext(default, null, SecurityGroupType.Anonymous);
        var pc = new PermissionContext(permissionGroupMap, sharedSecretKey: null);

        return (callerContext, pc);
    }
}