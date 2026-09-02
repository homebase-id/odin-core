using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core.Exceptions;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Authorization.Apps;
using Odin.Services.Drives.Management;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive;

/// <summary>
/// Addressing drives by slug: <c>/api/v2/apps/{appSlug}/drives/{driveSlug}</c>
/// (docs/drive-addressing.md).  The guid-addressed routes under <c>/api/v2/drives/{driveId}</c> are
/// unchanged and remain the canonical form; this is a second way to name the same drive.
/// </summary>
/// <remarks>
/// Resolution is two hops -- slug to app, then app plus slug to drive -- because a drive slug is
/// unique per app, not per identity.  That is what lets feed/news and chat/news be different drives,
/// and it is why neither half can be resolved alone.
///
/// A slug that names nothing gives 404, and so does one the caller may not see:
/// <see cref="IDriveManager.GetDrivesByAppIdAsync"/> filters by the caller's security level, so this
/// route cannot surface a drive the guid-addressed routes would hide.  The two cases are deliberately
/// not distinguished -- telling an unauthorized caller that a drive exists is itself a disclosure.
/// </remarks>
[ApiController]
[Route(UnifiedApiRouteConstants.AppDrivesRoot)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2AppDriveController(
    IDriveManager driveManager,
    IAppRegistrationService appRegistrationService)
    : OdinControllerBase
{
    /// <summary>
    /// Every drive the app owns.  <paramref name="type"/> filters by drive type slug, e.g.
    /// <c>?type=channel</c> -- which is what replaces <c>GET /drives/metadata/channel-drives</c>:
    /// that endpoint exists only because channel drives had no other way to be named as a group.
    /// </summary>
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.DriveMetadata])]
    public async Task<List<ClientDriveData>> GetAppDrives(
        [FromRoute] string appSlug,
        [FromQuery] string type = null)
    {
        var appId = await ResolveAppIdAsync(appSlug);
        var drives = await driveManager.GetDrivesByAppIdAsync(appId, WebOdinContext);

        // Filtered here rather than in a second lookup: an app owns a handful of drives, and the
        // list above is already in hand.
        if (!string.IsNullOrWhiteSpace(type))
        {
            drives = drives.Where(d => string.Equals(d.DriveTypeSlug, type, StringComparison.Ordinal)).ToList();
        }

        return drives.Select(ToClientDriveData).ToList();
    }

    /// <summary>One drive, addressed as <c>/apps/{appSlug}/drives/{driveSlug}</c>.</summary>
    [HttpGet("{driveSlug}")]
    [SwaggerOperation(Tags = [SwaggerInfo.DriveMetadata])]
    public async Task<ClientDriveData> GetAppDrive([FromRoute] string appSlug, [FromRoute] string driveSlug)
    {
        var appId = await ResolveAppIdAsync(appSlug);
        var drive = await driveManager.GetDriveBySlugAsync(appId, driveSlug, WebOdinContext);

        if (drive == null)
        {
            throw new OdinClientException($"No drive '{driveSlug}' on app '{appSlug}'",
                OdinClientErrorCode.InvalidDrive);
        }

        return ToClientDriveData(drive);
    }

    private async Task<Guid> ResolveAppIdAsync(string appSlug)
    {
        var app = await appRegistrationService.GetAppRegistrationBySlugAsync(appSlug, WebOdinContext);

        if (app == null)
        {
            // Same shape as an unknown drive slug, on purpose: which half of the address was wrong is
            // not something an unauthorized caller should be able to probe for.
            throw new OdinClientException($"No app '{appSlug}'", OdinClientErrorCode.InvalidDrive);
        }

        return app.AppId;
    }

    private static ClientDriveData ToClientDriveData(Odin.Services.Drives.StorageDrive drive) => new()
    {
        TargetDrive = drive.TargetDriveInfo,
        Name = drive.Name,
        Attributes = drive.Attributes,
        AppId = drive.AppId,
        DriveSlug = drive.DriveSlug,
        DriveTypeSlug = drive.DriveTypeSlug
    };
}
