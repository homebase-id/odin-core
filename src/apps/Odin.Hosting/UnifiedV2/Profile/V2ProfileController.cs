using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Odin.Hosting.Controllers.Base;
using Odin.Hosting.UnifiedV2.Authentication.Policy;
using Odin.Services.Profile;
using Odin.Services.Util;
using Swashbuckle.AspNetCore.Annotations;

namespace Odin.Hosting.UnifiedV2.Profile;

/// <summary>
/// Server-side writes of built-in profile attributes to the ProfileDrive. Reads stay client-side (clients
/// query attributes as files from the ProfileDrive), so this controller exposes only the write surface.
/// All writes funnel through <see cref="ProfileAttributeService"/>, which requires the ManageProfile
/// permission.
/// </summary>
[ApiController]
[Route(UnifiedApiRouteConstants.Profile)]
[UnifiedV2Authorize(UnifiedPolicies.OwnerOrApp)]
[ApiExplorerSettings(GroupName = "v2")]
public class V2ProfileController(
    ProfileAttributeService profileAttributeService
) : OdinControllerBase
{
    // PUT /api/v2/profile/attributes  — create or edit a built-in profile attribute (409 on a stale
    // version tag when editing).
    [SwaggerOperation(Tags = [SwaggerInfo.Profile])]
    [HttpPut("attributes")]
    [ProducesResponseType(typeof(ProfileAttributeWriteResponse), 200)]
    [ProducesResponseType(typeof(ProfileAttributeWriteConflict), 409)]
    public async Task<IActionResult> SetAttribute([FromBody] SetProfileAttributeRequest request)
    {
        OdinValidationUtils.AssertNotNull(request, nameof(request));

        var result = await profileAttributeService.SetAttributeAsync(request, WebOdinContext);
        if (result.Outcome == ProfileAttributeWriteOutcome.VersionConflict)
        {
            return Conflict(new ProfileAttributeWriteConflict { Id = result.Id, VersionTag = result.VersionTag });
        }

        return Ok(new ProfileAttributeWriteResponse { Id = result.Id, VersionTag = result.VersionTag });
    }

    // DELETE /api/v2/profile/attributes/{id}?versionTag=...  — delete an attribute (404 if missing,
    // 409 on a stale version tag).
    [SwaggerOperation(Tags = [SwaggerInfo.Profile])]
    [HttpDelete("attributes/{id:guid}")]
    public async Task<IActionResult> DeleteAttribute(Guid id, [FromQuery] Guid versionTag)
    {
        OdinValidationUtils.AssertNotEmptyGuid(id, nameof(id));

        var deleted = await profileAttributeService.DeleteAttributeAsync(id, versionTag, WebOdinContext);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
}

public sealed class ProfileAttributeWriteResponse
{
    public Guid Id { get; init; }
    public Guid VersionTag { get; init; }
}

public sealed class ProfileAttributeWriteConflict
{
    public Guid Id { get; init; }
    public Guid VersionTag { get; init; }
}
