using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Core;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.Controllers.ClientToken.Shared.Drive;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Base.SharedTypes;
using Odin.Services.Drives.Management;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Drive;

/// <summary>
/// Lists this identity's drives of one type: the V2 twin of V1's <c>drive/metadata/type</c>. Anonymous, as V1's guest
/// route is, so the public site can list channels. An anonymous caller sees only drives with anonymous reads on; a guest
/// sees every drive of the type that isn't owner-only or archived, including ones it can't read, as on V1. Anyone who is
/// not the owner or an app gets just each drive's address (<see cref="ClientDriveData.ForCaller"/>).
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.DrivesRoot)]
[UnifiedV2Authorize(UnifiedPolicies.Anonymous)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2DrivesByTypeController(IDriveManager driveManager) : OdinControllerBase
{
    [HttpGet]
    [SwaggerOperation(Tags = [SwaggerInfo.DriveMetadata])]
    public async Task<PagedResult<ClientDriveData>> GetDrivesByType(
        [FromQuery] Guid type,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 100)
    {
        var drives = await driveManager.GetDrivesAsync(type, new PageOptions(pageNumber, pageSize), WebOdinContext);
        var isOwner = WebOdinContext.Caller.IsOwner;
        var results = drives.Results.Select(d => ClientDriveData.ForCaller(d, isOwner)).ToList();
        return new PagedResult<ClientDriveData>(drives.Request, drives.TotalPages, results);
    }
}
