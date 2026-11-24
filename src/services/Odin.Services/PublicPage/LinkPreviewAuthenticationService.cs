using System;
using System.Linq;
using System.Threading.Tasks;
using Odin.Core;
using Odin.Services.Authorization;
using Odin.Services.Base;
using Odin.Services.Drives.Management;

namespace Odin.Services.PublicPage;

public class LinkPreviewAuthenticationService(IDriveManager driveManager, TenantContext tenantContext)
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

        return (AnonymousPermissions.Caller(),
            AnonymousPermissions.Create(anonymousDrives.Results, tenantContext.Settings, useTransitRead: false));
    }
}