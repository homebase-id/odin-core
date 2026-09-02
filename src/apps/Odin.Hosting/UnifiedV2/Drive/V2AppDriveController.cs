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
    /// Lists the drives an app owns on <b>your own</b> identity.
    /// </summary>
    /// <remarks>
    /// Use this to discover a drive's guid when all you know is its name.  Every entry carries both the
    /// address (<c>driveSlug</c>, <c>driveTypeSlug</c>) and the <c>targetDrive</c> guid pair the rest of
    /// the API takes, so one call here is enough to start using the guid routes.
    /// <para><b>Example</b> — <c>GET /api/v2/apps/feed/drives?type=channel</c> returns the Feed app's
    /// channel drives.  This replaces <c>GET /api/v2/drives/metadata/channel-drives</c>, which exists only
    /// because channel drives previously had no other way to be named as a group.</para>
    /// <para>Returns an empty list for an app that owns no drives, and 400 if no app holds
    /// <c>appSlug</c>.  Drives you may not see are omitted rather than reported.</para>
    /// </remarks>
    /// <param name="appSlug">The app's slug, e.g. <c>feed</c> or <c>chat</c>. Case-sensitive.</param>
    /// <param name="type">Optional drive type slug to filter on, e.g. <c>channel</c> or <c>profile</c>.
    /// Matched exactly against <c>driveTypeSlug</c>; an unknown value simply returns nothing.</param>
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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

    /// <summary>
    /// Resolves one drive on <b>your own</b> identity, addressed by app and drive slug.
    /// </summary>
    /// <remarks>
    /// The single-drive form of the listing above.  Returns the drive's <c>targetDrive</c> guid pair
    /// along with its name and attributes.
    /// <para><b>Example</b> — <c>GET /api/v2/apps/feed/drives/news</c>.</para>
    /// <para>400 when nothing answers to the address.  Unknown app, unknown drive, and a drive you may not
    /// see are deliberately indistinguishable.</para>
    /// </remarks>
    /// <param name="appSlug">The app's slug, e.g. <c>feed</c>. Case-sensitive.</param>
    /// <param name="driveSlug">The drive's slug within that app, e.g. <c>news</c>.  Unique per app, so
    /// <c>feed/news</c> and <c>chat/news</c> are different drives.</param>
    [HttpGet("{driveSlug}")]
    [SwaggerOperation(Tags = [SwaggerInfo.NewStuff])]
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
